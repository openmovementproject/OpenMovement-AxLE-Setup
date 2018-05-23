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
        const string LabelTapeWidth = "24"; // "3.5", "6", "9", "24"
        const string LabelExecutable = @"LabelPrint\PrintLabel.cmd";
        const string LabelArgs = @"Label-AxLE-{tapeWidth}mm.lbx objName {address} objBarcode {addressPlain}";

        volatile Queue<ulong> devices = new Queue<ulong>();
        private DateTime started;
        private ISet<ulong> whitelist = new HashSet<ulong>();
        private ISet<ulong> reported = new HashSet<ulong>();
        private int totalFound = 0;
        private int uniqueFound = 0;
        private int axleDevicesFound = 0;

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

            this.axleDevicesFound++;
            if (whitelist.Count > 0 && !whitelist.Contains(btAdv.BluetoothAddress))
            {
                Console.WriteLine("SCAN: Ignoring device not in whitelist: " + btAdv.Advertisement.LocalName + " " + " <" + btAdv.BluetoothAddress.FormatBtAddress() + ">");
                return;
            }

            devices.Enqueue(btAdv.BluetoothAddress);
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

                var address = devices.Dequeue();
                Console.WriteLine("-------------------------");
                Console.WriteLine($"AxLE FOUND: {address.FormatBtAddress()}");
                Console.WriteLine("Connecting...");

                BluetoothLEDevice device;
                try
                {
                    device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                    //if (device.Name != "axLE-Band")
                    //Console.WriteLine("SCAN: Found device: " + device.Name + " " + " <" + BtAddress(address.FormatBtAddress()) + ">");
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: Problem connecting to device" + address.FormatBtAddress() + ": " + e.Message);
                    devices.Enqueue(address);   // retry later
                    return false;
                }


                var serial = device.BluetoothAddress.ToString("X");
                Console.WriteLine($"Serial: {serial} <{device.BluetoothAddress.FormatBtAddress()}>");

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
                            var macAddress = device.BluetoothAddress.FormatBtAddress();
                            string labelArgs = "" + LabelArgs;
                            labelArgs = labelArgs.Replace("{address}", macAddress);
                            labelArgs = labelArgs.Replace("{addressPlain}", macAddress.Replace(":", ""));
                            labelArgs = labelArgs.Replace("{tapeWidth}", LabelTapeWidth);
                            Console.WriteLine("MAC: " + address);
                            RedirectedProcess.Execute(LabelExecutable, labelArgs);
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
            // If whitelist is empty, all devices are scanned
//            whitelist.Add("D9:B1:A1:83:DB:6C".ParseBtAddress());    // desk
//            whitelist.Add("FD:5A:F1:5D:4E:90".ParseBtAddress());    // GW
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