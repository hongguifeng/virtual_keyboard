using VirtualKeyboard.Core.Diagnostics;
using VirtualKeyboard.Core.Input;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class InputFailureFeedbackTests
{
    [Theory]
    [InlineData(0x2000, 0x3000, IntegrityComparisonStatus.TargetHigher)]
    [InlineData(0x2000, 0x2000, IntegrityComparisonStatus.SameOrLower)]
    [InlineData(0x3000, 0x2000, IntegrityComparisonStatus.SameOrLower)]
    public void InspectorComparesNumericIntegrityRids(
        int currentRid,
        int targetRid,
        IntegrityComparisonStatus expected)
    {
        var api = new FakeIntegrityApi(new Dictionary<int, (bool Success, int Rid, int Error)>
        {
            [10] = (true, currentRid, 0),
            [20] = (true, targetRid, 0),
        });

        IntegrityComparisonResult result = new ProcessIntegrityInspector(api, 10).CompareToCurrentProcess(20);

        Assert.Equal(expected, result.Status);
        Assert.Equal(currentRid, result.CurrentIntegrityRid);
        Assert.Equal(targetRid, result.TargetIntegrityRid);
        Assert.Equal([10, 20], api.RequestedProcessIds);
    }

    [Theory]
    [InlineData(5, IntegrityComparisonStatus.AccessDenied)]
    [InlineData(87, IntegrityComparisonStatus.Unknown)]
    public void InspectorPreservesTargetOpenFailure(int error, IntegrityComparisonStatus expected)
    {
        var api = new FakeIntegrityApi(new Dictionary<int, (bool Success, int Rid, int Error)>
        {
            [10] = (true, 0x2000, 0),
            [20] = (false, 0, error),
        });

        IntegrityComparisonResult result = new ProcessIntegrityInspector(api, 10).CompareToCurrentProcess(20);

        Assert.Equal(expected, result.Status);
        Assert.Equal(0x2000, result.CurrentIntegrityRid);
        Assert.Equal(0, result.TargetIntegrityRid);
        Assert.Equal(error, result.ErrorCode);
    }

    [Fact]
    public void CurrentProcessAccessFailureIsUnknownNotTargetBoundary()
    {
        var api = new FakeIntegrityApi(new Dictionary<int, (bool Success, int Rid, int Error)>
        {
            [10] = (false, 0, 5),
        });

        IntegrityComparisonResult result = new ProcessIntegrityInspector(api, 10).CompareToCurrentProcess(20);

        Assert.Equal(IntegrityComparisonStatus.Unknown, result.Status);
        Assert.Equal(5, result.ErrorCode);
        Assert.Equal([10], api.RequestedProcessIds);
    }

    [Fact]
    public void InspectorConvertsNativeLoadingFailureToUnknown()
    {
        var api = new FakeIntegrityApi(
            new Dictionary<int, (bool Success, int Rid, int Error)>(),
            new DllNotFoundException("synthetic"));
        IntegrityComparisonResult result = new ProcessIntegrityInspector(api, 10).CompareToCurrentProcess(20);
        Assert.Equal(IntegrityComparisonStatus.Unknown, result.Status);
        Assert.Equal(50, result.ErrorCode);
    }

    [Fact]
    public void SystemInspectorCanReadCurrentProcessToken()
    {
        IntegrityComparisonResult result = new ProcessIntegrityInspector().CompareToCurrentProcess(Environment.ProcessId);

        Assert.Equal(IntegrityComparisonStatus.SameOrLower, result.Status);
        Assert.True(result.CurrentIntegrityRid > 0);
        Assert.Equal(result.CurrentIntegrityRid, result.TargetIntegrityRid);
        Assert.Equal(0, result.ErrorCode);
    }

    [Fact]
    public void SuccessCreatesNoFeedbackDiagnosticOrIntegrityProbe()
    {
        using var diagnostics = new DiagnosticLogger();
        var probes = 0;
        var factory = new InputFailureFeedbackFactory(
            _ => { probes++; return SameIntegrity(); },
            diagnostics);

        InputFailureFeedback feedback = factory.Create(
            new InputSendResult(InputSendStatus.Succeeded, 2, 2, 0),
            42);

        Assert.Equal(InputFailureKind.None, feedback.Kind);
        Assert.False(feedback.ShouldDisplay);
        Assert.Equal(string.Empty, feedback.Message);
        Assert.Equal(0, probes);
        Assert.False(diagnostics.TryReadNext(out _));
    }

    [Theory]
    [InlineData(IntegrityComparisonStatus.TargetHigher, InputFailureKind.PermissionBoundary, "目标权限高于本程序，无法输入")]
    [InlineData(IntegrityComparisonStatus.AccessDenied, InputFailureKind.PossiblePermissionBoundary, "可能存在目标权限边界，无法输入")]
    [InlineData(IntegrityComparisonStatus.SameOrLower, InputFailureKind.SendFailed, "输入发送失败")]
    [InlineData(IntegrityComparisonStatus.Unknown, InputFailureKind.SendFailed, "输入发送失败")]
    public void ZeroSendUsesIntegrityAwareFixedFeedback(
        IntegrityComparisonStatus integrityStatus,
        InputFailureKind expectedKind,
        string expectedMessage)
    {
        var factory = new InputFailureFeedbackFactory(_ => new(integrityStatus, 0x2000, 0x3000, 0));

        InputFailureFeedback feedback = factory.Create(
            new InputSendResult(InputSendStatus.Failed, 2, 0, 5),
            42);

        Assert.Equal(expectedKind, feedback.Kind);
        Assert.True(feedback.ShouldDisplay);
        Assert.Equal(expectedMessage, feedback.Message);
    }

    [Fact]
    public void PartialSendIsNeverReportedAsSuccessAndIsNotRetriedHere()
    {
        var probes = 0;
        var factory = new InputFailureFeedbackFactory(_ =>
        {
            probes++;
            return SameIntegrity();
        });

        InputFailureFeedback feedback = factory.Create(
            new InputSendResult(InputSendStatus.PartialFailure, 6, 3, 5),
            42);

        Assert.Equal(InputFailureKind.PartialDelivery, feedback.Kind);
        Assert.Equal("输入未完整发送；为避免重复，未自动重试", feedback.Message);
        Assert.Equal(1, probes);
    }

    [Theory]
    [InlineData(InputSendStatus.TargetInvalid, InputFailureKind.TargetChanged, "目标已变化，输入已取消")]
    [InlineData(InputSendStatus.NativeUnavailable, InputFailureKind.NativeUnavailable, "系统输入功能不可用")]
    [InlineData(InputSendStatus.InvalidInput, InputFailureKind.InvalidRequest, "输入动作无效")]
    [InlineData(InputSendStatus.Cancelled, InputFailureKind.Cancelled, "输入已取消")]
    [InlineData(InputSendStatus.SafetyFaulted, InputFailureKind.SafetyFaulted, "输入引擎已安全停止，请退出后重启")]
    public void NonDeliveryStatusesUseFixedFeedbackWithoutOpeningProcess(
        InputSendStatus status,
        InputFailureKind expectedKind,
        string expectedMessage)
    {
        var probes = 0;
        var factory = new InputFailureFeedbackFactory(_ =>
        {
            probes++;
            return SameIntegrity();
        });

        InputFailureFeedback feedback = factory.Create(new InputSendResult(status, 0, 0, 0), 42);

        Assert.Equal(expectedKind, feedback.Kind);
        Assert.Equal(expectedMessage, feedback.Message);
        Assert.Equal(0, probes);
    }

    [Fact]
    public void ClassificationDiagnosticContainsOnlyNumericDeliveryMetadataAndClosedReason()
    {
        using var diagnostics = new DiagnosticLogger();
        var factory = new InputFailureFeedbackFactory(
            _ => new(IntegrityComparisonStatus.TargetHigher, 0x2000, 0x3000, 0),
            diagnostics);

        _ = factory.Create(new InputSendResult(InputSendStatus.Failed, 4, 0, 5), 42);

        Assert.True(diagnostics.TryReadNext(out DiagnosticEvent? classified));
        Assert.Equal(DiagnosticType.InputFailureClassified, classified!.Type);
        Assert.Equal(DiagnosticModule.Input, classified.Module);
        Assert.Equal(42, classified.TargetProcessId);
        Assert.Equal(ReasonCode.PermissionBoundary, classified.Reason);
        Assert.Equal(4, classified.RequestedCount);
        Assert.Equal(0, classified.CompletedCount);
        Assert.Equal(5, classified.ErrorCode);
        Assert.False(diagnostics.TryReadNext(out _));
    }

    private static IntegrityComparisonResult SameIntegrity() =>
        new(IntegrityComparisonStatus.SameOrLower, 0x2000, 0x2000, 0);

    private sealed class FakeIntegrityApi(
        IReadOnlyDictionary<int, (bool Success, int Rid, int Error)> results,
        Exception? failure = null) : IProcessIntegrityNativeApi
    {
        public List<int> RequestedProcessIds { get; } = [];

        public bool TryGetIntegrityRid(int processId, out int integrityRid, out int errorCode)
        {
            RequestedProcessIds.Add(processId);
            if (failure is not null) throw failure;
            (bool success, int rid, int error) = results[processId];
            integrityRid = rid;
            errorCode = error;
            return success;
        }
    }
}
