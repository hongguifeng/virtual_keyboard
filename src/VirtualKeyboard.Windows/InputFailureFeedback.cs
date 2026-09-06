using System.Runtime.InteropServices;
using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;

namespace VirtualKeyboard.Windows;

public enum IntegrityComparisonStatus
{
    SameOrLower,
    TargetHigher,
    AccessDenied,
    Unknown,
}

public readonly record struct IntegrityComparisonResult(
    IntegrityComparisonStatus Status,
    int CurrentIntegrityRid,
    int TargetIntegrityRid,
    int ErrorCode);

public sealed class ProcessIntegrityInspector
{
    private readonly IProcessIntegrityNativeApi _api;
    private readonly int _currentProcessId;

    public ProcessIntegrityInspector()
        : this(new SystemProcessIntegrityNativeApi(), Environment.ProcessId)
    {
    }

    internal ProcessIntegrityInspector(IProcessIntegrityNativeApi api, int currentProcessId)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentProcessId);
        _currentProcessId = currentProcessId;
    }

    public IntegrityComparisonResult CompareToCurrentProcess(int targetProcessId)
    {
        if (targetProcessId <= 0)
        {
            return new(IntegrityComparisonStatus.Unknown, 0, 0, 87);
        }

        try
        {
            if (!_api.TryGetIntegrityRid(_currentProcessId, out int currentRid, out int currentError))
            {
                return new(
                    IntegrityComparisonStatus.Unknown,
                    0,
                    0,
                    currentError);
            }
            if (!_api.TryGetIntegrityRid(targetProcessId, out int targetRid, out int targetError))
            {
                return new(
                    targetError == 5 ? IntegrityComparisonStatus.AccessDenied : IntegrityComparisonStatus.Unknown,
                    currentRid,
                    0,
                    targetError);
            }

            return new(
                targetRid > currentRid
                    ? IntegrityComparisonStatus.TargetHigher
                    : IntegrityComparisonStatus.SameOrLower,
                currentRid,
                targetRid,
                0);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new(IntegrityComparisonStatus.Unknown, 0, 0, 50);
        }
    }
}

public enum InputFailureKind
{
    None,
    TargetChanged,
    PermissionBoundary,
    PossiblePermissionBoundary,
    PartialDelivery,
    SendFailed,
    NativeUnavailable,
    InvalidRequest,
    Cancelled,
}

public readonly record struct InputFailureFeedback(
    InputFailureKind Kind,
    bool ShouldDisplay,
    string Message);

public sealed class InputFailureFeedbackFactory
{
    private readonly Func<int, IntegrityComparisonResult> _compareIntegrity;
    private readonly DiagnosticLogger? _diagnostics;

    public InputFailureFeedbackFactory(
        ProcessIntegrityInspector integrityInspector,
        DiagnosticLogger? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(integrityInspector);
        _compareIntegrity = integrityInspector.CompareToCurrentProcess;
        _diagnostics = diagnostics;
    }

    internal InputFailureFeedbackFactory(
        Func<int, IntegrityComparisonResult> compareIntegrity,
        DiagnosticLogger? diagnostics = null)
    {
        _compareIntegrity = compareIntegrity ?? throw new ArgumentNullException(nameof(compareIntegrity));
        _diagnostics = diagnostics;
    }

    public InputFailureFeedback Create(InputSendResult result, int targetProcessId)
    {
        if (result.IsSuccess)
        {
            return new(InputFailureKind.None, false, string.Empty);
        }

        InputFailureFeedback feedback;
        ReasonCode reason = ReasonCode.Unknown;
        if (result.Status is InputSendStatus.Failed or InputSendStatus.PartialFailure)
        {
            IntegrityComparisonResult integrity = _compareIntegrity(targetProcessId);
            if (integrity.Status == IntegrityComparisonStatus.TargetHigher)
            {
                reason = ReasonCode.PermissionBoundary;
                feedback = new(
                    InputFailureKind.PermissionBoundary,
                    true,
                    "目标权限高于本程序，无法输入");
            }
            else if (integrity.Status == IntegrityComparisonStatus.AccessDenied)
            {
                reason = ReasonCode.PermissionBoundary;
                feedback = new(
                    InputFailureKind.PossiblePermissionBoundary,
                    true,
                    "可能存在目标权限边界，无法输入");
            }
            else if (result.Status == InputSendStatus.PartialFailure)
            {
                feedback = new(
                    InputFailureKind.PartialDelivery,
                    true,
                    "输入未完整发送；为避免重复，未自动重试");
            }
            else
            {
                feedback = new(InputFailureKind.SendFailed, true, "输入发送失败");
            }
        }
        else
        {
            feedback = result.Status switch
            {
                InputSendStatus.TargetInvalid => new(InputFailureKind.TargetChanged, true, "目标已变化，输入已取消"),
                InputSendStatus.NativeUnavailable => new(InputFailureKind.NativeUnavailable, true, "系统输入功能不可用"),
                InputSendStatus.InvalidInput => new(InputFailureKind.InvalidRequest, true, "输入动作无效"),
                InputSendStatus.Cancelled => new(InputFailureKind.Cancelled, true, "输入已取消"),
                _ => new(InputFailureKind.SendFailed, true, "输入发送失败"),
            };
            reason = result.Status switch
            {
                InputSendStatus.TargetInvalid => ReasonCode.ValidationFailed,
                InputSendStatus.Cancelled => ReasonCode.Cancelled,
                _ => ReasonCode.Unknown,
            };
        }

        _diagnostics?.Log(
            DiagnosticType.InputFailureClassified,
            DiagnosticModule.Input,
            targetProcessId: targetProcessId,
            reason: reason,
            errorCode: Math.Max(0, result.ErrorCode),
            requestedCount: Math.Max(0, result.RequestedEvents),
            completedCount: Math.Max(0, result.SentEvents));
        return feedback;
    }
}

internal interface IProcessIntegrityNativeApi
{
    bool TryGetIntegrityRid(int processId, out int integrityRid, out int errorCode);
}

internal sealed class SystemProcessIntegrityNativeApi : IProcessIntegrityNativeApi
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int MaximumTokenInformationBytes = 64 * 1024;

    public bool TryGetIntegrityRid(int processId, out int integrityRid, out int errorCode)
    {
        integrityRid = 0;
        errorCode = 0;
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, checked((uint)processId));
        if (process == nint.Zero)
        {
            errorCode = Marshal.GetLastPInvokeError();
            return false;
        }

        nint token = nint.Zero;
        nint buffer = nint.Zero;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out token))
            {
                errorCode = Marshal.GetLastPInvokeError();
                return false;
            }

            _ = GetTokenInformation(token, TokenIntegrityLevel, nint.Zero, 0, out uint requiredBytes);
            if (requiredBytes == 0 || requiredBytes > MaximumTokenInformationBytes)
            {
                errorCode = Marshal.GetLastPInvokeError();
                if (errorCode == 0) errorCode = 87;
                return false;
            }

            buffer = Marshal.AllocHGlobal(checked((int)requiredBytes));
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, requiredBytes, out _))
            {
                errorCode = Marshal.GetLastPInvokeError();
                return false;
            }

            TokenMandatoryLabel label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            if (label.Label.Sid == nint.Zero || !IsValidSid(label.Label.Sid))
            {
                errorCode = 87;
                return false;
            }
            nint countPointer = GetSidSubAuthorityCount(label.Label.Sid);
            if (countPointer == nint.Zero)
            {
                errorCode = 87;
                return false;
            }
            byte count = Marshal.ReadByte(countPointer);
            if (count == 0)
            {
                errorCode = 87;
                return false;
            }

            nint ridPointer = GetSidSubAuthority(label.Label.Sid, checked((uint)(count - 1)));
            if (ridPointer == nint.Zero)
            {
                errorCode = Marshal.GetLastPInvokeError();
                if (errorCode == 0) errorCode = 87;
                return false;
            }
            integrityRid = Marshal.ReadInt32(ridPointer);
            if (integrityRid <= 0)
            {
                errorCode = 87;
                return false;
            }
            return true;
        }
        finally
        {
            if (buffer != nint.Zero) Marshal.FreeHGlobal(buffer);
            if (token != nint.Zero) _ = CloseHandle(token);
            _ = CloseHandle(process);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public nint Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        int tokenInformationClass,
        nint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsValidSid(nint sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern nint GetSidSubAuthority(nint sid, uint subAuthorityIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
