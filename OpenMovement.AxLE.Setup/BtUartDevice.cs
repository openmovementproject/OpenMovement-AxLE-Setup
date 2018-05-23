#if __IGNORE_THIS_FILE__

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using System.Text;

namespace OpenMovement.AxLE.Setup
{
    class BtUartDevice : IDisposable
    {
        #region ServiceUUIDs
        public static readonly Guid UartServiceUuid = new Guid("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
        public static readonly Guid UartRxCharacUuid = new Guid("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");
        public static readonly Guid UartTxCharacUuid = new Guid("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
        #endregion


        #region Disposal
        ~BtUartDevice()
        {
            Dispose(false);
        }

        protected bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;
            if (disposing)
            {
                if (this.device != null)
                {
                    this.device.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion


        private ulong address;
        private BluetoothLEDevice device;
        private GattCharacteristic txCharac;
        private GattCharacteristic rxCharac;
        private TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> rxEventHandler = null;
        private MemoryStream received;

        public BtUartDevice(ulong address)
        {
            this.address = address;
            this.received = new MemoryStream();
        }

        public async Task Connect()
        {
            // Connect to device
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(this.address);

            // Enumerate services, UART service and TX/RX characteristics.
            var gatt = await this.device.GetGattServicesAsync();
            var characs = await gatt.Services.Single(s => s.Uuid == UartServiceUuid).GetCharacteristicsAsync();
            var rxCharac = characs.Characteristics.Single(c => c.Uuid == UartRxCharacUuid);
            var txCharac = characs.Characteristics.Single(c => c.Uuid == UartTxCharacUuid);

            // Notify on receive
            await rxCharac.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

            // Connected
            this.device = device;
            this.rxCharac = rxCharac;
            this.txCharac = txCharac;

            // Handle incoming data
            this.rxEventHandler = new TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>(delegate (GattCharacteristic c, GattValueChangedEventArgs args)
            {
                // Data as bytes
                var data = new byte[args.CharacteristicValue.Length];
                DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);

                // Append data to our incoming buffer
                lock (received)
                {
                    received.Write(data, 0, data.Length);
                }

                // TODO: Raise event for incoming data...
            });
            this.rxCharac.ValueChanged += rxEventHandler;

            // Detect disconnection
            device.ConnectionStatusChanged += Device_ConnectionStatusChanged;
            Device_ConnectionStatusChanged(null, null); // Avoid slight race condition if already disconnected before event handler added
        }

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (this.device != null && this.device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                this.rxCharac.ValueChanged -= rxEventHandler;
                this.device = null;
                this.rxCharac = null;
                this.txCharac = null;
            }
        }

        private async Task Write(byte[] data)
        {
            await txCharac.WriteValueAsync(data.AsBuffer());
        }

        private async Task Write(string stringData)
        {
            byte[] data = Encoding.GetEncoding("ISO-8859-1").GetBytes(stringData);
            await Write(stringData);
        }



        public string ReadBufferedLine()
        {
            byte[] lineData;
            // Find LF in buffer
            lock (received)
            {
                byte[] buffer = received.GetBuffer();
                int lineLength = Array.IndexOf(buffer, '\n');
                if (lineLength < 0) { return null; }
                lineData = ReadBuffered(lineLength + 1);
            }
            // Skip CR (if CR/LF sequence)
            int skipCr = (lineData.Length > 0 && lineData[lineData.Length - 1] == '\r') ? 1 : 0;
            string line = Encoding.GetEncoding("ISO-8859-1").GetString(lineData, 0, lineData.Length - skipCr);
            return line;
        }

        public byte[] ReadBuffered(int max = -1)
        {
            byte[] data;
            lock (received)
            {
                int count = received.Length > int.MaxValue ? int.MaxValue : (int)received.Length;
                if (max >= 0 && count > max) { count = max; }
                data = new byte[count];
                byte[] buffer = received.GetBuffer();
                System.Buffer.BlockCopy(buffer, 0, data, 0, count);
                System.Buffer.BlockCopy(buffer, count, buffer, 0, (int)received.Length - count);
                received.SetLength(received.Length - count);
            }
            return data;
        }

    }
}

#endif
