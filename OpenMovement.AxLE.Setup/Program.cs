using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Security.Cryptography;

namespace OpenMovement.AxLE.Setup
{
    class Program
    {
        private LabelPrint labelPrinter = new LabelPrint(@"LabelPrint\PrintLabel.cmd", "Label-AxLE-9mm.lbx objName $address objBarcode $address");

        private DateTime started;
        volatile Queue<BluetoothLEDevice> devices = new Queue<BluetoothLEDevice>();
        private ISet<ulong> whitelist = new HashSet<ulong>();
        volatile ISet<ulong> reported = new HashSet<ulong>();
        volatile int totalFound = 0;
        volatile int uniqueFound = 0;
        volatile int axleDevicesFound = 0;

        public static ulong BtAddress(string address)
        {
            address = address.Replace(":", "").Replace("-", "").ToUpper();    // remove address separators
            if (address.Length != 12)
            {
                throw new FormatException("MAC address must be 12 nibbles.");
            }
            return ulong.Parse(address, System.Globalization.NumberStyles.HexNumber);
        }

        public static string BtAddress(ulong address)
        {
            return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                    (address >> (5 * 8)) & 0xff,
                    (address >> (4 * 8)) & 0xff,
                    (address >> (3 * 8)) & 0xff,
                    (address >> (2 * 8)) & 0xff,
                    (address >> (1 * 8)) & 0xff,
                    (address >> (0 * 8)) & 0xff
                );
        }

        private async void ProcessDevice(BluetoothLEAdvertisementReceivedEventArgs btAdv)
        {
            BluetoothLEDevice device;
            try
            {
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(btAdv.BluetoothAddress);                
                if (device.Name != "axLE-Band")
                {
                    Console.WriteLine("WARNING: Device has wrong name: " + device.Name + " <" + BtAddress(btAdv.BluetoothAddress) + ">");
                }
                this.axleDevicesFound++;
                if (whitelist.Count > 0 && !whitelist.Contains(btAdv.BluetoothAddress))
                {
                    Console.WriteLine("SCAN: Ignoring device not in whitelist: " + device.Name + " " + " <" + BtAddress(btAdv.BluetoothAddress) + ">");
                    return;
                }
                //Console.WriteLine("SCAN: Found device: " + device.Name + " " + " <" + BtAddress(btAdv.BluetoothAddress) + ">");
                devices.Enqueue(device);
            }
            catch (Exception e)
            {
                Console.WriteLine("SCAN: Exception while retrieving device " + btAdv + ": " + e.Message);
            }
        }

        private void BleWatcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs btAdv)
        {
            this.totalFound++;
            if (reported.Contains(btAdv.BluetoothAddress)) { return; }  // ignore duplicate
            reported.Add(btAdv.BluetoothAddress);   // Add to ignore in future
            this.uniqueFound++;

            if (btAdv.AdvertisementType != BluetoothLEAdvertisementType.ConnectableUndirected)
            {
                //Console.WriteLine("SCAN: Ignoring non-connectable/directed: " + " <" + BtAddress(btAdv.BluetoothAddress) + "> (" + btAdv.AdvertisementType + ")");
                return;
            }

            if (btAdv.Advertisement.LocalName != "axLE-Band")
            {
                //Console.WriteLine("SCAN: Ignoring device : " + btAdv.Advertisement.LocalName + " <" + BtAddress(btAdv.BluetoothAddress) + "> (" + btAdv.AdvertisementType + ")");
                return;
            }

            //Console.WriteLine("SCAN: Fetching device: " + " <" + BtAddress(btAdv.BluetoothAddress) + ">");
            ProcessDevice(btAdv);
        }

        async Task<bool> MainTasks()
        {
            try
            {
                if (Console.KeyAvailable)
                {
                    if (Console.ReadKey().Key == ConsoleKey.X)
                    {
                        Console.WriteLine("Exiting...");
                        return true;
                    }
                    Console.WriteLine("...resuming...");
                }

                if (devices.Count == 0)
                {
                    Console.WriteLine($"...scanning ({(int)(DateTime.Now - this.started).TotalSeconds} s, {totalFound} reports, {uniqueFound} addresses, {axleDevicesFound} AxLE devices)...");
                    Thread.Sleep(3000);
                    return false;
                }

                var device = devices.Dequeue();
                Console.WriteLine("-------------------------");
                Console.WriteLine($"AxLE FOUND: {BtAddress(device.BluetoothAddress)}");
                var serial = device.BluetoothAddress.ToString("X");
                Console.WriteLine($"Serial: {serial} <{BtAddress(device.BluetoothAddress)}>");

                var pass = serial.Substring(serial.Length - 6);
                Console.WriteLine("Attempting Auth...");

                var gatt = await device.GetGattServicesAsync();
                var characs = await gatt.Services.Single(s => s.Uuid == AxLEUuid.UartServiceUuid).GetCharacteristicsAsync();

                var rxCharac = characs.Characteristics.Single(c => c.Uuid == AxLEUuid.UartRxCharacUuid);
                var txCharac = characs.Characteristics.Single(c => c.Uuid == AxLEUuid.UartTxCharacUuid);

                await rxCharac.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

                if (!await Authenticate(rxCharac, txCharac, pass))
                {
                    Console.WriteLine("AUTH FAILED!! Device must be wiped to continue proceed? (Y/N)");
                    if (Console.ReadKey().Key == ConsoleKey.Y)
                    {
                        Console.WriteLine("Resetting device...");
                        await Reset(rxCharac, txCharac, pass);
                        Thread.Sleep(500);
                        Console.WriteLine("Device reset. Authenticating...");
                        await Authenticate(rxCharac, txCharac, pass);
                    }
                    else
                    {
                        Console.WriteLine("Skipping device...");
                        return false;
                    }
                }

                Console.WriteLine($"Auth Success!!");

                Console.WriteLine("*** Flashing and buzzing device, press Y if found, N to skip device ***");
                for (; ; )
                {
                    await Flash(txCharac);
                    await Buzz(txCharac);
                    if (Console.KeyAvailable)
                    {
                        Console.Write("Found+print? (Y/N) >");
                        var key = Console.ReadKey().Key;
                        if (key == ConsoleKey.N)
                        {
                            Console.WriteLine("Skipping...");
                            break;
                        }
                        else if (key == ConsoleKey.Y)
                        {
                            Console.WriteLine("Printing...");
                            // Print MAC address
                            var address = BtAddress(device.BluetoothAddress);
                            Console.WriteLine("MAC: " + address);
                            labelPrinter.LabelPrintSync(address);
                            break;
                        }
                    }
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: " + e.Message);
            }
            return false;
        }

        async Task Run()
        {
            this.started = DateTime.Now;
            var bleWatcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };
            bleWatcher.Received += BleWatcher_Received;

#if DEBUG
            whitelist.Add(BtAddress("D9:B1:A1:83:DB:6C"));
#endif

            // Start the scanner
            try
            {
                bleWatcher.Start();
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: Problem starting scanner: " + e.Message);
                return;
            }

            try
            {
                while (!await MainTasks());
            }
            finally
            {
                bleWatcher.Stop();
            }
        }

        private async Task<bool> Authenticate(GattCharacteristic rxCharac, GattCharacteristic txCharac, string pass)
        {
            var tcs = new TaskCompletionSource<bool>();
            var buffer = CryptographicBuffer.ConvertStringToBinary($"U{pass}", BinaryStringEncoding.Utf8);

            TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> callback = null;
            callback = new TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>(delegate (GattCharacteristic c, GattValueChangedEventArgs args)
            {
                var response = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, args.CharacteristicValue);

                rxCharac.ValueChanged -= callback;

                tcs.SetResult(response.Contains("Authenticated"));
            });

            rxCharac.ValueChanged += callback;

            await txCharac.WriteValueAsync(buffer);

            return await tcs.Task;
        }

        private async Task Reset(GattCharacteristic rxCharac, GattCharacteristic txCharac, string pass)
        {
            var buffer = CryptographicBuffer.ConvertStringToBinary($"E{pass}", BinaryStringEncoding.Utf8);
            await txCharac.WriteValueAsync(buffer);
        }

        private async Task Flash(GattCharacteristic txCharac)
        {
            var buffer = CryptographicBuffer.ConvertStringToBinary($"3", BinaryStringEncoding.Utf8);
            await txCharac.WriteValueAsync(buffer);
        }

        private async Task Buzz(GattCharacteristic txCharac)
        {
            var buffer = CryptographicBuffer.ConvertStringToBinary($"M", BinaryStringEncoding.Utf8);
            await txCharac.WriteValueAsync(buffer);
        }

        static void Main(string[] args)
        {
            Program program = new Program();
            Task.Run(program.Run).Wait();
#if DEBUG
            Console.WriteLine("END: Press any key to continue...");
            Console.ReadKey();
#endif
        }


    }
}