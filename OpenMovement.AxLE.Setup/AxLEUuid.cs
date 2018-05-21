using System;

namespace OpenMovement.AxLE.Setup
{
    public struct AxLEUuid
    {
        public static readonly Guid UartServiceUuid = new Guid("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
        public static readonly Guid BootloaderServiceUuid = new Guid();

        public static readonly Guid UartRxCharacUuid = new Guid("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");
        public static readonly Guid UartTxCharacUuid = new Guid("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");

        public static readonly Guid DeviceInformationServiceUuid = new Guid("0000180A-0000-1000-8000-00805f9b34fb");

        public static readonly Guid SerialNumberCharacUuid = new Guid("00002A25-0000-1000-8000-00805f9b34fb");
    }
}
