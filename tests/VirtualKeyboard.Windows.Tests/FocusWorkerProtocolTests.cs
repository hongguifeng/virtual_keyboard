using System.IO;
using System.Text;
using System.Text.Json;
using VirtualKeyboard.Core.Geometry;
using VirtualKeyboard.Core.Targeting;
using VirtualKeyboard.Windows;

namespace VirtualKeyboard.Windows.Tests;

public sealed class FocusWorkerProtocolTests
{
    [Theory]
    [InlineData(FocusControlType.ComboBox)]
    [InlineData(FocusControlType.Spinner)]
    public void RoundTripPreservesIdentityGeometryAndPasswordFlagWithParentAssignedVersion(FocusControlType type)
    {
        var result = new FocusWorkerResult(4, 1, true, 42, 100, [1, 2], type,
            true, true, false, true, FocusTargetEvaluationStatus.Evaluated, Editability.Editable,
            ClassificationReasonCode.ValuePattern, 101, new PhysicalPixelRect(-500, 20, 200, 30), false, 12);
        var packet = FocusWorkerPacket.Parse(JsonSerializer.Serialize(new FocusWorkerPacket(3, 4, 1, 1, result, 0)));
        var evaluation = packet.Result!.ToEvaluation(900);
        Assert.Equal(900, evaluation.Snapshot.Version);
        Assert.Equal(900, evaluation.Classification.Version);
        Assert.Equal(result.Anchor, evaluation.Anchor);
        Assert.Equal(new RuntimeIdentity([1, 2]), evaluation.Snapshot.RuntimeId);
        Assert.True(evaluation.Snapshot.IsPassword);
        Assert.Equal(type, evaluation.Snapshot.ControlType);
    }

    [Fact]
    public async Task OversizedTransportIsRejectedBeforeUnboundedAllocation()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', FocusWorkerPacket.MaximumLineLength + 1)));
        using var reader = new StreamReader(stream);
        await Assert.ThrowsAsync<InvalidDataException>(() => IsolatedFocusObservationService.ReadBoundedLineAsync(reader, CancellationToken.None));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"Progress\":-1}")]
    [InlineData("{\"Stage\":9}")]
    public void MalformedMetadataIsRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => FocusWorkerPacket.Parse(json));
}
