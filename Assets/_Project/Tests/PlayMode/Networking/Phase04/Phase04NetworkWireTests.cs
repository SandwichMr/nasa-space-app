// No test asmdef exists, so this file compiles into Assembly-CSharp (it needs the internal 'T' 'B' codec); the guard keeps
// it out of player builds. Pure byte-level tests of D3's phase 4 cable: no transport, socket or process.
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using B = Trainer.Unity.Networking.BaseOperations.BaseOperationsWireProtocol;
using F = Phase04NetworkFingerprint;

public sealed class Phase04NetworkWireTests
{
    private static readonly PlayerId Ana = new PlayerId(1), Leo = new PlayerId(2), Kai = new PlayerId(3);
    private const double Trend = 0.123456789; // distinctive value searched in the encoded bytes

    /// <summary>Fixed ids keep two encodings of nearly the same state identical up to the field under test.</summary>
    private static Guid Id(int n) => new Guid(n, 0, 0, new byte[8]);

    // ---- a state that uses every phase 4 field (D2 fixtures, synthetic values) --------------------------------

    private static BaseOperationsSnapshot Rich(Action<BaseOperationsSnapshotBuilder> tweak = null)
    {
        BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
        BaseOperationsSnapshot initial = BaseOperationsFixtures.Initial(scenario, 2, Ana, Leo, Kai);
        BaseOperationsSnapshotBuilder b = BaseOperationsFixtures.Edit(scenario, initial)
            .Expedition(e => e.AtTime(150).Session(SessionState.Running));
        var waterObservation = new EquipmentObservation(3, EquipmentKind.WaterProcessor, Ana, 120, 180, 5,
            Findings.ProductOffSpec | Findings.ProcessorFilterRestricted,
            new[] { new ObservedReading("product-conductivity", InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, 80, 120)) }, true);
        b.Equipment(new EquipmentStatus(EquipmentKind.WaterProcessor, scenario.WaterStation, EquipmentOperatingState.Isolated,
            EquipmentKnowledge.Degraded, Findings.ProductOffSpec, 5, waterObservation,
            new RepairJob(EquipmentKind.WaterProcessor, RepairSteps.Inspect | RepairSteps.Isolate | RepairSteps.Intervene | RepairSteps.Test,
                100, 3, false)));
        var airObservation = new EquipmentObservation(4, EquipmentKind.AirRevitalization, Leo, 140, 200, 2,
            Findings.ActiveBedSaturated | Findings.StandbyBedLoaded, new[]
            {
                new ObservedReading("active-bed-loading", InstrumentReading.Valid(ReadingUnit.Fraction, 0.7, 140)),
                new ObservedReading("standby-bed-loading", new InstrumentReading(ReadingStatus.Stale, ReadingUnit.Fraction, 0.4, 100)),
                new ObservedReading("inlet-flow", InstrumentReading.Unavailable(ReadingUnit.Fraction))
            }, true);
        b.Equipment(new EquipmentStatus(EquipmentKind.AirRevitalization, scenario.AirStation, EquipmentOperatingState.Online,
            EquipmentKnowledge.Nominal, Findings.ActiveBedSaturated, 2, airObservation, null));
        b.Water(new WaterTreatmentStatus(1.5, InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, 80, 130), 40,
            PotableQuality.Suspect, 0.5, 3));
        b.Spare(new SpareStock(BaseOperationsFixtures.InletFilter, 0, 3));
        b.Operation(new OperationProgress(Id(1), scenario.WaterStation, BaseOperationAction.Intervene,
            OperationChoice.ReplaceProcessorFilter, Ana, 5, 20, true, BaseOperationsFixtures.ProcessorFilter));
        b.Operation(new OperationProgress(Id(2), scenario.AirStation, BaseOperationAction.Inspect, OperationChoice.None, Leo,
            1, 4, false, null));
        ResupplyManifest m = scenario.Manifests[0];
        b.Order(new ResupplyOrder(1, m.Key, Ana, 0, 30, 100, OrderState.Unloaded, Array.Empty<ManifestItem>(), 120));
        b.Order(new ResupplyOrder(2, m.Key, Kai, 10, 40, 190, OrderState.Cancelled, Array.Empty<ManifestItem>(), 20));
        b.Order(new ResupplyOrder(3, m.Key, Leo, 1, 31, 149, OrderState.Arrived, new[] { ManifestItem.Mass(ResourceKind.PotableWater, 4.5) }, null));
        b.Order(new ResupplyOrder(4, m.Key, Ana, 140, 170, 320, OrderState.Pending, m.Items, null));
        b.Activity(new ActivityRecord(1, ActivityKind.Resupply, m.Key, ActivityStatus.Completed, Ana, 0, 120, "delivery-unloaded"));
        b.Activity(new ActivityRecord(2, ActivityKind.Expedition, "regolith-sample", ActivityStatus.Cancelled, PlayerId.None, 50, 60, null));
        b.Activity(new ActivityRecord(3, ActivityKind.Repair, "water-recycler", ActivityStatus.Active, Ana, 100, null, null));
        b.Decision(new DecisionRecord(Id(3), Ana, BaseOperationAction.Intervene, OperationChoice.ReplaceProcessorFilter,
            EquipmentKind.WaterProcessor, 110, DecisionAssessment.Appropriate, "filter-replaced"));
        b.Decision(new DecisionRecord(Id(4), Leo, BaseOperationAction.RouteProduct, OperationChoice.AcceptToPotable,
            EquipmentKind.WaterProcessor, 131, DecisionAssessment.Harmful, "accepted-off-spec-product"));
        b.Report(new OperationsReport(1, 0, 120, 2, 1, -1.5, -0.8, -3000.25));
        b.Session(new BaseSessionStatus(BaseSessionMode.Continuous, 12.5, 60, IrreversibleCause.None, null, 4, 1)); // sequence 4: a 4th record changes only the list
        tweak?.Invoke(b);
        BaseOperationsSnapshot draft = b.Build();
        // Explicit gauges: one with a trend, one stale (a stale reading keeps its value and time; it is never zero).
        ResourceGauge[] gauges = draft.Gauges.ToArray();
        ResourceGauge power = gauges[(int)GaugeKind.PowerBalance];
        gauges[(int)GaugeKind.PowerBalance] = ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.PowerBalance), power.Reading, null, Trend);
        ResourceGauge co2 = gauges[(int)GaugeKind.CabinCarbonDioxide];
        gauges[(int)GaugeKind.CabinCarbonDioxide] = ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.CabinCarbonDioxide), co2.Reading.AsStale(), null, null);
        return new BaseOperationsSnapshot(draft.Expedition, draft.Session, gauges, draft.Equipment, draft.Water, draft.Spares,
            draft.Operations, draft.Orders, draft.Activities, draft.Decisions, draft.LatestReport);
    }

    private static BaseOperationsSnapshot Decode(byte[] message)
    {
        Assert.That(B.TryDecodeFromHost(message, out BaseOperationsHostMessage decoded, out string error), Is.True, error);
        Assert.That(decoded.Kind, Is.EqualTo(OutpostMessageKind.Snapshot));
        return decoded.Snapshot;
    }

    private static string HostError(byte[] message)
    {
        Assert.That(B.TryDecodeFromHost(message, out _, out string error), Is.False, "must be refused");
        return error;
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) return i;
        return Math.Min(a.Length, b.Length);
    }

    private static byte[] Patched(byte[] message, int index, params byte[] bytes)
    {
        var copy = (byte[])message.Clone();
        Buffer.BlockCopy(bytes, 0, copy, index, bytes.Length);
        return copy;
    }

    // ---- snapshots ---------------------------------------------------------------------------------------------

    [Test]
    public void AFullPhase4SnapshotCrossesTheCableExactly()
    {
        BaseOperationsSnapshot s = Rich();
        byte[] message = B.EncodeSnapshot(s);
        Assert.That(message.Take(4), Is.EqualTo(new byte[] { 0x54, 0x42, 1, 11 }), "'T' 'B' v1 Snapshot");
        BaseOperationsSnapshot back = Decode(message);
        Assert.That(F.Of(back), Is.EqualTo(F.Of(s)), "every field, every double bit for bit");
        Assert.That(back.GaugeOf(GaugeKind.CabinCarbonDioxide).Reading.Status, Is.EqualTo(ReadingStatus.Stale));
        Assert.That(back.GaugeOf(GaugeKind.CabinCarbonDioxide).Reading.Value, Is.Not.Null, "stale is not zero or unknown");
        Assert.That(back.EquipmentOf(EquipmentKind.AirRevitalization).LatestObservation.Readings[2].Reading.Value, Is.Null);
        Assert.That(back.FreeSpares(BaseOperationsFixtures.ProcessorFilter), Is.EqualTo(0), "the reservation travels with the state");
    }

    [Test]
    public void AnEndedSessionAndClosedActivitiesCrossTheCable()
    {
        // Base 9b96f80 never stores ActivityRecord/BaseSessionStatus.EndedAtSeconds (reported to D2): without the decoder's
        // probe every snapshot with a finished activity or an irreversible end would be refused and clients would freeze.
        BaseOperationsScenario scenario = BaseOperationsFixtures.Scenario();
        BaseOperationsSnapshot s = BaseOperationsFixtures.Edit(scenario, BaseOperationsFixtures.Initial(scenario, 1, Ana, Leo))
            .Expedition(e => e.AtTime(200).Ended(ExpeditionOutcome.EssentialServiceLost))
            .Activity(new ActivityRecord(1, ActivityKind.WaterBatch, "water-recycler", ActivityStatus.Completed, Ana, 10, 30, "batch-processed"))
            .Activity(new ActivityRecord(2, ActivityKind.Resupply, BaseOperationsFixtures.StandardManifest, ActivityStatus.Failed, Leo, 40, 190, null))
            .Session(new BaseSessionStatus(BaseSessionMode.Continuous, 60, 60, IrreversibleCause.EssentialPowerLost, 190, 2, 0))
            .Build();
        BaseOperationsSnapshot back = Decode(B.EncodeSnapshot(s));
        Assert.That(F.Of(back), Is.EqualTo(F.Of(s)));
        Assert.That(back.Outpost.State, Is.EqualTo(SessionState.Interrupted));
        Assert.That(back.Session.EndCause, Is.EqualTo(IrreversibleCause.EssentialPowerLost));
        Assert.That(back.Activities.Select(a => a.Status), Is.EqualTo(new[] { ActivityStatus.Completed, ActivityStatus.Failed }));
        Assert.That(back.Session.EndedAtSeconds, Is.EqualTo(s.Session.EndedAtSeconds), "whatever the base stores, the client holds the same");
        Assert.That(B.ActivityEndTimeDropped, Is.EqualTo(s.Activities[0].EndedAtSeconds == null), "the probe reflects the real contract");
        Assert.That(B.SessionEndTimeDropped, Is.EqualTo(s.Session.EndedAtSeconds == null));
    }

    [Test]
    public void ThePhase2And3PartsAreTheUnchangedOlderBodies()
    {
        BaseOperationsSnapshot s = Rich();
        byte[] phase4 = B.EncodeSnapshot(s);
        byte[] phase3 = ExpeditionWireProtocol.EncodeSnapshot(s.Expedition);
        byte[] phase2 = OutpostWireProtocol.EncodeSnapshot(s.Outpost);
        Assert.That(phase4.Skip(4).Take(phase3.Length - 4), Is.EqualTo(phase3.Skip(4)), "'T' 'E' body, byte for byte");
        Assert.That(phase3.Skip(4).Take(phase2.Length - 4), Is.EqualTo(phase2.Skip(4)), "'T' 'O' body, byte for byte");
        Assert.That(phase4.Length, Is.GreaterThan(phase3.Length));
    }

    [Test]
    public void EveryTruncationAndATrailingByteAreRefused()
    {
        byte[] message = B.EncodeSnapshot(Rich());
        for (int length = 4; length < message.Length; length++)
            Assert.That(HostError(message.Take(length).ToArray()), Is.EqualTo(SessionNetworkCodes.MalformedPayload), $"length {length}");
        Assert.That(HostError(message.Concat(new byte[] { 0 }).ToArray()), Is.EqualTo(SessionNetworkCodes.MalformedPayload));
    }

    [Test]
    public void ValuesTheContractRefusesAreRefusedOnTheCable()
    {
        byte[] good = B.EncodeSnapshot(Rich());

        // NaN where the contract wants a finite trend.
        byte[] trend = BitConverter.GetBytes(Trend);
        int at = Enumerable.Range(0, good.Length - 8).Single(i => good.Skip(i).Take(8).SequenceEqual(trend));
        Assert.That(HostError(Patched(good, at, BitConverter.GetBytes(double.NaN))), Is.EqualTo(SessionNetworkCodes.MalformedPayload));

        // A visible symptom bit that does not exist (VisibleSymptoms has no constructor check: the cable checks it).
        EquipmentStatus quiet = Rich().EquipmentOf(EquipmentKind.WaterProcessor).WithSymptoms(Findings.None);
        byte[] other = B.EncodeSnapshot(Rich(b => b.Equipment(quiet)));
        int symptoms = FirstDifference(good, other);
        Assert.That(good[symptoms], Is.EqualTo((byte)Findings.ProductOffSpec));
        Assert.That(HostError(Patched(good, symptoms, 0x40)), Is.EqualTo(SessionNetworkCodes.MalformedPayload));

        // A potable quality that is not an enum value.
        other = B.EncodeSnapshot(Rich(b => b.Water(new WaterTreatmentStatus(1.5, InstrumentReading.Valid(ReadingUnit.MicrosiemensPerCentimeter, 80, 130),
            40, PotableQuality.Verified, 0, 3))));
        int quality = FirstDifference(good, other);
        Assert.That(HostError(Patched(good, quality, 7)), Is.EqualTo(SessionNetworkCodes.MalformedPayload));

        // A presence flag that is not 0 or 1 (the report).
        other = B.EncodeSnapshot(Rich(b => b.Report(null)));
        int report = FirstDifference(good, other);
        Assert.That(good[report], Is.EqualTo(1));
        Assert.That(HostError(Patched(good, report, 2)), Is.EqualTo(SessionNetworkCodes.MalformedPayload));

        // More activities than the contract allows.
        other = B.EncodeSnapshot(Rich(b => b.Activity(new ActivityRecord(4, ActivityKind.ScienceAssignment, "regolith-sample",
            ActivityStatus.Completed, Leo, 10, 20, "science-returned"))));
        int count = FirstDifference(good, other);
        Assert.That(good[count], Is.EqualTo(3));
        Assert.That(HostError(Patched(good, count, (byte)(BaseOperationsLimits.MaxActivities + 1))), Is.EqualTo(SessionNetworkCodes.MalformedPayload));

        // A negative player id.
        Guid fixedId = Guid.NewGuid();
        byte[] byAna = B.EncodeSnapshot(Rich(b => b.Decision(new DecisionRecord(fixedId, Ana, BaseOperationAction.Isolate, OperationChoice.None,
            EquipmentKind.WaterProcessor, 100, DecisionAssessment.Appropriate, "isolated"))));
        byte[] byLeo = B.EncodeSnapshot(Rich(b => b.Decision(new DecisionRecord(fixedId, Leo, BaseOperationAction.Isolate, OperationChoice.None,
            EquipmentKind.WaterProcessor, 100, DecisionAssessment.Appropriate, "isolated"))));
        Assume.That(byAna.Length, Is.EqualTo(byLeo.Length));
        int player = FirstDifference(byAna, byLeo);
        Assert.That(HostError(Patched(byAna, player, 0xFF, 0xFF, 0xFF, 0xFF)), Is.EqualTo(SessionNetworkCodes.MalformedPayload));
    }

    [Test]
    public void ForeignCablesVersionsAndKindsAreRefused()
    {
        byte[] good = B.EncodeSnapshot(Rich());
        Assert.That(HostError(Patched(good, 1, 0x45)), Is.EqualTo(SessionNetworkCodes.BadMagic), "a 'T' 'E' header");
        Assert.That(HostError(Patched(good, 1, 0x4F)), Is.EqualTo(SessionNetworkCodes.BadMagic), "a 'T' 'O' header");
        Assert.That(HostError(Patched(good, 2, 2)), Is.EqualTo(SessionNetworkCodes.UnsupportedVersion));
        Assert.That(HostError(Array.Empty<byte>()), Is.EqualTo(SessionNetworkCodes.EmptyPayload));
        Assert.That(HostError(new byte[] { 0x54, 0x42, 1, 99 }), Is.EqualTo(SessionNetworkCodes.UnknownMessageKind));
        Assert.That(HostError(B.EncodeJoin()), Is.EqualTo(SessionNetworkCodes.UnexpectedMessageKind), "a client kind from the host");
        Assert.That(HostError(new byte[] { 0x54, 0x42, 1, 4 }), Is.EqualTo(SessionNetworkCodes.UnexpectedMessageKind), "an Operation from the host");
        Assert.That(ExpeditionWireProtocol.TryDecodeFromHost(good, out _, out string e3), Is.False);
        Assert.That(e3, Is.EqualTo(SessionNetworkCodes.BadMagic), "the phase 3 decoder refuses the phase 4 cable");
        Assert.That(OutpostWireProtocol.TryDecodeFromHost(good, out _, out string e2), Is.False);
        Assert.That(e2, Is.EqualTo(SessionNetworkCodes.BadMagic), "the phase 2 decoder refuses the phase 4 cable");
    }

    [Test]
    public void ResultsAndMembershipsKeepTheirPhase2Bodies()
    {
        var result = new CommandResult(Guid.NewGuid(), 3, false, BaseOperationReasons.StaleObservation);
        Assert.That(B.TryDecodeFromHost(B.EncodeResult(result, out bool truncated), out BaseOperationsHostMessage r, out _));
        Assert.That(truncated, Is.False);
        Assert.That((r.Result.CommandId, r.Result.Epoch, r.Result.Accepted, r.Result.Reason),
            Is.EqualTo((result.CommandId, 3L, false, BaseOperationReasons.StaleObservation)));
        Assert.That(B.TryDecodeFromHost(B.EncodeMembership(OutpostMembership.Joined(Leo)), out BaseOperationsHostMessage m, out _));
        Assert.That(m.Membership.IsJoined && m.Membership.Player == Leo);
        Assert.That(B.EncodeResult(result, out _).Skip(4), Is.EqualTo(ExpeditionWireProtocol.EncodeResult(result, out _).Skip(4)));
    }

    // ---- client messages ---------------------------------------------------------------------------------------

    private static BaseOperationsClientMessage FromClient(byte[] payload)
    {
        Assert.That(B.TryDecodeFromClient(payload, out BaseOperationsClientMessage message, out string error), Is.True, error);
        return message;
    }

    private static string ClientError(byte[] payload)
    {
        Assert.That(B.TryDecodeFromClient(payload, out _, out string error), Is.False, "must be refused");
        return error;
    }

    [Test]
    public void OperationsTravelWithEveryStampAndNoIdentity()
    {
        var commands = new[]
        {
            BaseOperationCommand.Operation(Guid.NewGuid(), 2, BaseOperationAction.Intervene, BaseOperationsFixtures.AirStation,
                OperationChoice.ReplaceInletFilter, 7, 3),
            BaseOperationCommand.PlaceOrder(Guid.NewGuid(), 2, BaseOperationsFixtures.LogisticsConsole, BaseOperationsFixtures.StandardManifest, 1),
            BaseOperationCommand.ForOrder(Guid.NewGuid(), 0, BaseOperationAction.UnloadDelivery, BaseOperationsFixtures.LogisticsConsole, 4),
            BaseOperationCommand.Operation(Guid.NewGuid(), 5, BaseOperationAction.CancelOperation, BaseOperationsFixtures.WaterStation),
            // Malformed values the authority must answer (UnknownAction / InvalidActionValue): the cable carries them as they are.
            new BaseOperationCommand(Guid.NewGuid(), 1, (BaseOperationAction)999, StationId.None, (OperationChoice)77, null, 9, 9, 9),
            new BaseOperationCommand(Guid.Empty, 1, BaseOperationAction.Inspect, BaseOperationsFixtures.AirStation)
        };
        foreach (BaseOperationCommand c in commands)
        {
            byte[] payload = B.EncodeOperation(c);
            Assert.That(payload.Take(4), Is.EqualTo(new byte[] { 0x54, 0x42, 1, 4 }));
            Assert.That(payload.Length, Is.LessThanOrEqualTo(B.MaxClientMessageBytes));
            BaseOperationsClientMessage m = FromClient(payload);
            Assert.That(m.Kind, Is.EqualTo(B.Operation));
            Assert.That(m.Operation.HasSameContent(c), $"{c.Action} round trip");
            Assert.That(m.Operation.ShapeError(), Is.EqualTo(c.ShapeError()), "shape judged by the authority, not changed on the way");
        }
    }

    [Test]
    public void PhaseThreeIntentsAndMovementKeepTheirBodies()
    {
        ExpeditionCommand c = ExpeditionCommand.CycleAirlock(Guid.NewGuid(), 2, ExpeditionFixtures.InnerPanel, 4, 6);
        BaseOperationsClientMessage m = FromClient(B.EncodeSubmit(c));
        Assert.That(m.Kind, Is.EqualTo(OutpostMessageKind.Submit));
        Assert.That(m.Command.HasSameContent(c));
        Assert.That(B.EncodeSubmit(c).Skip(4), Is.EqualTo(ExpeditionWireProtocol.EncodeSubmit(c).Skip(4)), "same Submit body as 'T' 'E'");
        var input = new MovementInput(2, 9, MoveDirection.FromAxes(0.6, -0.8));
        m = FromClient(B.EncodeMovement(input));
        Assert.That((m.Movement.Epoch, m.Movement.Sequence, m.Movement.Direction), Is.EqualTo((2L, 9L, input.Direction)));
        Assert.That(FromClient(B.EncodeJoin()).Kind, Is.EqualTo(OutpostMessageKind.Join));
    }

    [Test]
    public void MalformedClientMessagesAreRefused()
    {
        byte[] good = B.EncodeOperation(BaseOperationCommand.Operation(Guid.NewGuid(), 2, BaseOperationAction.Intervene,
            BaseOperationsFixtures.AirStation, OperationChoice.ReplaceInletFilter, 7, 3));
        Assert.That(ClientError(good.Take(good.Length - 1).ToArray()), Is.EqualTo(SessionNetworkCodes.MalformedPayload));
        Assert.That(ClientError(good.Concat(new byte[] { 1 }).ToArray()), Is.EqualTo(SessionNetworkCodes.MalformedPayload));
        Assert.That(ClientError(Patched(good, 4 + 16, BitConverter.GetBytes(-5L))), Is.EqualTo(SessionNetworkCodes.MalformedPayload), "negative epoch");
        int stationLength = 4 + 16 + 8 + 4;
        Assert.That(ClientError(Patched(good, stationLength + 1, (byte)'A')), Is.EqualTo(SessionNetworkCodes.MalformedPayload), "not a stable key");
        Assert.That(ClientError(Patched(good, good.Length - 16, BitConverter.GetBytes(-2L))), Is.EqualTo(SessionNetworkCodes.MalformedPayload),
            "observation stamp below Unstated");
        Assert.That(ClientError(Patched(good, 1, 0x45)), Is.EqualTo(SessionNetworkCodes.BadMagic));
        Assert.That(ClientError(new byte[] { 0x54, 0x42, 1, 11 }), Is.EqualTo(SessionNetworkCodes.UnexpectedMessageKind), "a client cannot send a snapshot");
        Assert.That(ClientError(new byte[] { 0x54, 0x42, 1, 13 }), Is.EqualTo(SessionNetworkCodes.UnexpectedMessageKind), "nor chunks");
        Assert.That(ClientError(new byte[] { 0x54, 0x42, 1, 42 }), Is.EqualTo(SessionNetworkCodes.UnknownMessageKind));
        Assert.That(ClientError(new byte[B.MaxClientMessageBytes + 1]), Is.EqualTo(SessionNetworkCodes.PayloadTooLarge));
        byte[] move = B.EncodeMovement(new MovementInput(0, 1, MoveDirection.FromAxes(1, 0)));
        Assert.That(ClientError(Patched(move, 4 + 16, BitConverter.GetBytes(double.NaN))), Is.EqualTo(SessionNetworkCodes.MalformedPayload));
        var unencodable = new BaseOperationCommand(Guid.NewGuid(), 0, BaseOperationAction.PlaceOrder, BaseOperationsFixtures.LogisticsConsole,
            OperationChoice.None, "Not A Key");
        Assert.That(B.CanCarry(unencodable), Is.False);
        Assert.Throws<ArgumentException>(() => B.EncodeOperation(unencodable));
    }

    // ---- framing and size ----------------------------------------------------------------------------------------

    [Test]
    public void LargeSnapshotsAreChunkedAndReassembledOnlyWhole()
    {
        BaseOperationsSnapshot s = Rich();
        byte[] message = B.EncodeSnapshot(s);
        Assert.That(message.Length, Is.GreaterThan(OutpostWireProtocol.MaxFrameBytes), "this state needs chunks");
        List<byte[]> frames = B.Frame(message);
        Assert.That(frames.Count, Is.GreaterThanOrEqualTo(3));
        Assert.That(frames.All(f => f.Length <= OutpostWireProtocol.MaxFrameBytes && f[0] == 0x54 && f[1] == 0x42 && f[3] == 13));
        var assembler = new B.Assembler();
        byte[] whole = null;
        for (int i = 0; i < frames.Count; i++)
        {
            bool done = assembler.TryAccept(frames[i], out whole, out string error);
            Assert.That(done, Is.EqualTo(i == frames.Count - 1), error);
        }
        Assert.That(whole, Is.EqualTo(message));

        // A gap drops the partial message; the next complete one is accepted.
        Assert.That(assembler.TryAccept(frames[0], out _, out _), Is.False);
        Assert.That(assembler.TryAccept(frames[2], out _, out string gap), Is.False);
        Assert.That(gap, Is.EqualTo(SessionNetworkCodes.MalformedPayload));
        byte[] small = B.EncodeMembership(OutpostMembership.Joined(Ana));
        Assert.That(B.Frame(small).Single(), Is.EqualTo(small), "a message that fits is one unchanged frame");
        Assert.That(assembler.TryAccept(small, out byte[] alone, out _) && alone == small);

        // Chunks of another cable and an oversize total are refused.
        List<byte[]> phase3 = ExpeditionWireProtocol.Frame(ExpeditionWireProtocol.EncodeSnapshot(s.Expedition));
        Assert.That(new B.Assembler().TryAccept(phase3[0], out _, out string magic), Is.False);
        Assert.That(magic, Is.EqualTo(SessionNetworkCodes.BadMagic));
        var oversize = new B.Assembler();
        string last = null;
        const int total = 40; // 40 × 1092 B > 32 KB
        for (int i = 0; i < total && last == null; i++)
        {
            var frame = new byte[OutpostWireProtocol.MaxFrameBytes];
            frame[0] = 0x54; frame[1] = 0x42; frame[2] = 1; frame[3] = 13;
            frame[4] = (byte)i; frame[6] = total;
            oversize.TryAccept(frame, out _, out last);
        }
        Assert.That(last, Is.EqualTo(SessionNetworkCodes.PayloadTooLarge));
    }

    /// <summary>Analytic upper bound of the phase 4 extension, from the contract limits (keys ≤ 32 ASCII bytes + length).</summary>
    private static int Phase4ExtensionBound()
    {
        const int key = OutpostLimits.MaxKeyLength + 1, optional = 9, reading = 1 + 1 + optional + optional;
        int machines = Enum.GetValues(typeof(EquipmentKind)).Length;
        int observation = 8 + 1 + 4 + 8 + 8 + 8 + 1 + 1 + BaseOperationsLimits.MaxReadingsPerObservation * (key + reading) + 1;
        int repair = 1 + 1 + 8 + 8 + 1;
        int session = 1 + 8 + 8 + 1 + optional + 8 + 4;
        int gauges = 1 + BaseOperationsLimits.MaxGauges * (1 + reading + 4 * optional + 1);
        int equipment = 1 + machines * (1 + key + 1 + 1 + 1 + 8 + 1 + observation + 1 + repair);
        int water = 8 + reading + 8 + 1 + 8 + 8;
        int spares = 1 + BaseEquipmentLimits.MaxSpareKinds * (key + 4 + 4);
        int operations = 1 + BaseOperationsLimits.MaxOperations * (16 + key + 1 + 1 + 4 + 8 + 8 + 1 + key);
        int item = 1 + Math.Max(key + 4, 1 + 8);
        int orders = 1 + BaseOperationsLimits.MaxOrders * (8 + key + 4 + 8 * 3 + 1 + 1 + BaseOperationsLimits.MaxManifestItems * item + optional);
        int activities = 1 + BaseOperationsLimits.MaxActivities * (8 + 1 + key + 1 + 4 + 8 + optional + key);
        int decisions = 1 + BaseOperationsLimits.MaxDecisions * (16 + 4 + 1 + 1 + 1 + 8 + 1 + key);
        int report = 1 + 8 + 8 + 8 + 4 + 4 + 8 * 3;
        return session + gauges + equipment + water + spares + operations + orders + activities + decisions + report;
    }

    [Test]
    public void TheWorstCaseSnapshotFitsTheCableBound()
    {
        // Any phase 3 snapshot fits the 16 KB bound of the 'T' 'E' cable (its framing refuses more); add the phase 4 bound.
        int bound = OutpostWireProtocol.MaxMessageBytes + Phase4ExtensionBound();
        Assert.That(bound, Is.LessThanOrEqualTo(B.MaxMessageBytes), $"worst case {bound} B");
        Assert.That(B.MaxMessageBytes / (OutpostWireProtocol.MaxFrameBytes - 8) + 1, Is.LessThan(ushort.MaxValue), "chunk counter");

        // A heavy state near the limits: the measured phase 4 extension stays under the analytic bound and round-trips.
        string Long(string head) => (head + "-" + new string('x', OutpostLimits.MaxKeyLength)).Substring(0, OutpostLimits.MaxKeyLength);
        BaseOperationsSnapshot heavy = Rich(b =>
        {
            for (long a = 4; a <= BaseOperationsLimits.MaxActivities; a++)
                b.Activity(new ActivityRecord(a, ActivityKind.ScienceAssignment, Long("activity" + a), ActivityStatus.Completed, Kai, 1, 2, Long("result" + a)));
            for (int d = 0; d < BaseOperationsLimits.MaxDecisions - 2; d++)
                b.Decision(new DecisionRecord(Guid.NewGuid(), Kai, BaseOperationAction.ServiceAir, OperationChoice.SwitchBed,
                    EquipmentKind.AirRevitalization, 145, DecisionAssessment.NotIndicated, Long("explanation" + d)));
            for (long o = 5; o <= BaseOperationsLimits.MaxOrders; o++)
                b.Order(new ResupplyOrder(o, BaseOperationsFixtures.StandardManifest, Leo, 140, 170, 320, OrderState.Pending,
                    BaseOperationsFixtures.Scenario().Manifests[0].Items, null));
        });
        byte[] phase4 = B.EncodeSnapshot(heavy);
        int extension = phase4.Length - ExpeditionWireProtocol.EncodeSnapshot(heavy.Expedition).Length;
        Assert.That(extension, Is.LessThanOrEqualTo(Phase4ExtensionBound()));
        Assert.That(F.Of(Decode(phase4)), Is.EqualTo(F.Of(heavy)));
        Console.WriteLine($"heavy phase 4 snapshot: {phase4.Length} B (extension {extension} B); analytic worst case ≤ {bound} B; bound {B.MaxMessageBytes} B");
    }
}

internal static class Phase04NetworkWireTestExtensions
{
    /// <summary>The same status with other visible symptoms (test-only copy).</summary>
    public static EquipmentStatus WithSymptoms(this EquipmentStatus s, Findings symptoms) =>
        new EquipmentStatus(s.Equipment, s.Station, s.Operating, s.Knowledge, symptoms, s.StateSequence, s.LatestObservation, s.Repair);
}
#endif
