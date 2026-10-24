using System;
using System.Collections.Generic;
using System.Linq;
using LogAnalyzer.Dfir.Devices;
using LogAnalyzer.Dfir.Registers;
using Xunit;

namespace LogAnalyzer.UI.Tests
{
    /// <summary>Every USB storage session in the control period must match a transfer register entry (same medium, same day or +-1 day).</summary>
    public class TransferReconciliationTests
    {
        private static readonly DateOnly From = new(2026, 10, 1), To = new(2026, 10, 31);

        private static DateTimeOffset At(int day, int hour = 12) => new(new DateTime(2026, 10, day, hour, 0, 0, DateTimeKind.Local));

        private static DeviceRecord Stick(string serial, DeviceKind kind = DeviceKind.UsbStorage, params DateTimeOffset[] connections) =>
            new() { Kind = kind, InstanceId = @"USBSTOR\X\" + serial, Serial = serial, Connections = connections };

        private static TransferRow Transfer(string nr, string serial, string day, string reg = "") =>
            new() { RegistrationNumber = nr, DateTime = day + " 10:00", MediumSerial = serial, MediumRegistration = reg };

        [Fact]
        public void Same_day_transfer_matches()
        {
            var r = TransferReconciliation.CheckUsbSessions([Stick("AA11", DeviceKind.UsbStorage, At(10))], [Transfer("T1", "AA11", "2026-10-10")], From, To);
            var s = Assert.Single(r);
            Assert.Equal(UsbSessionStatus.Matched, s.Status);
            Assert.Equal("T1", s.TransferNumber);
        }

        [Theory]
        [InlineData("2026-10-09", UsbSessionStatus.Matched)]
        [InlineData("2026-10-11", UsbSessionStatus.Matched)]
        [InlineData("2026-10-08", UsbSessionStatus.ToVerify)]
        [InlineData("2026-10-12", UsbSessionStatus.ToVerify)]
        public void One_day_tolerance_only(string transferDay, UsbSessionStatus expected)
        {
            var r = TransferReconciliation.CheckUsbSessions([Stick("AA11", DeviceKind.UsbStorage, At(10))], [Transfer("T1", "AA11", transferDay)], From, To);
            Assert.Equal(expected, Assert.Single(r).Status);
        }

        [Fact]
        public void Transfer_on_another_medium_does_not_match_and_message_is_plain()
        {
            var r = TransferReconciliation.CheckUsbSessions([Stick("AA11", DeviceKind.UsbStorage, At(10))], [Transfer("T1", "BB22", "2026-10-10")], From, To);
            var s = Assert.Single(r);
            Assert.Equal(UsbSessionStatus.ToVerify, s.Status);
            Assert.Equal("DE_VERIFICAT", s.StatusText);
            Assert.Equal("mediu de stocare folosit fără înregistrare în registrul de transferuri", s.Message);
        }

        [Fact]
        public void Serial_suffix_and_case_are_normalised_and_registry_number_also_matches()
        {
            var dev = Stick("aa11&0", DeviceKind.UsbStorage, At(10));
            Assert.Equal(UsbSessionStatus.Matched, Assert.Single(TransferReconciliation.CheckUsbSessions([dev], [Transfer("T1", "AA11", "2026-10-10")], From, To)).Status);
            var noSerial = Stick("", DeviceKind.UsbStorage, At(10));
            var byReg = TransferReconciliation.CheckUsbSessions([noSerial], [Transfer("T2", "", "2026-10-10", reg: "1234-001L")], From, To, _ => "1234-001L");
            Assert.Equal(UsbSessionStatus.Matched, Assert.Single(byReg).Status);
        }

        [Fact]
        public void Connections_outside_the_period_are_ignored()
        {
            var dev = Stick("AA11", DeviceKind.UsbStorage, new DateTimeOffset(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Local)), At(10));
            var r = TransferReconciliation.CheckUsbSessions([dev], [], From, To);
            Assert.Equal(At(10), Assert.Single(r).Connected);
        }

        [Fact]
        public void Non_storage_devices_are_ignored_and_memory_cards_count()
        {
            var devices = new List<DeviceRecord>
            {
                Stick("P1", DeviceKind.PortableDevice, At(10)), Stick("B1", DeviceKind.Bluetooth, At(10)), Stick("D1", DeviceKind.InternalDisk, At(10)),
                Stick("O1", DeviceKind.Optical, At(10)), Stick("C1", DeviceKind.MemoryCard, At(10)),
            };
            var r = TransferReconciliation.CheckUsbSessions(devices, [], From, To);
            Assert.Equal("C1", Assert.Single(r).Device.Serial);
        }

        [Fact]
        public void Each_connection_is_judged_on_its_own()
        {
            var dev = Stick("AA11", DeviceKind.UsbStorage, At(5), At(20));
            var r = TransferReconciliation.CheckUsbSessions([dev], [Transfer("T1", "AA11", "2026-10-05")], From, To);
            Assert.Equal([UsbSessionStatus.Matched, UsbSessionStatus.ToVerify], r.Select(x => x.Status).ToArray());
        }
    }
}
