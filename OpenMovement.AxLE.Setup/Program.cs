using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace OpenMovement.AxLE.Setup
{
    class Program
    {
        static async void Main(string[] args)
        {
            var devices = new Queue<BluetoothLEDevice>();

            var bleWatcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            bleWatcher.Received += async (w, btAdv) => {
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(btAdv.BluetoothAddress);

                if (device.Name != "axLE-Band") return;

                devices.Enqueue(device);
            };

            bleWatcher.Start();

            var input = "";

            while (input.ToLower() != "exit")
            {
                if (devices.Any())
                {
                    var device = devices.Dequeue();
                    var serial = device.BluetoothAddress.ToString("X");
                    var pass = serial.Substring(serial.Length - 6);

                    Console.WriteLine($"AxLE FOUND: {serial}");
                    Console.WriteLine("Attempting Auth...");

                    var gatt = await device.GetGattServicesAsync();
                    var characs = await gatt.Services.Single(s => s.Uuid == AxLEUuid.UartServiceUuid).GetCharacteristicsAsync();

                    var rxCharac = characs.Characteristics.Single(c => c.Uuid == AxLEUuid.UartRxCharacUuid);
                    var txCharac = characs.Characteristics.Single(c => c.Uuid == AxLEUuid.UartTxCharacUuid);

                    await rxCharac.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

                    if (!await Authenticate(rxCharac, txCharac, pass))
                    {
                        Console.WriteLine("AUTH FAILED!! Device must be wiped to continue proceed? (Y/N)");
                        var key = Console.ReadKey();

                        if (key.Key == ConsoleKey.Y)
                        {
                            Console.WriteLine("Resetting device...");
                            await Reset(rxCharac, txCharac, pass);
                            Thread.Sleep(500);
                            Console.WriteLine("Device reset. Authenticating...");
                            await Authenticate(rxCharac, txCharac, pass);
                        }
                    }

                    Console.WriteLine($"Auth Success!!");
                    Console.WriteLine("Now flashing and buzzing device...");

                    await Flash(txCharac);
                    await Buzz(txCharac);

                    input = Console.ReadLine();
                }
            }
        }

        private static async Task<bool> Authenticate(GattCharacteristic rxCharac, GattCharacteristic txCharac, string pass)
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

            return tcs.Task;
        }

        private static async Task<bool> Reset(GattCharacteristic rxCharac, GattCharacteristic txCharac, string pass)
        {
            var tcs = new TaskCompletionSource<bool>();
            var buffer = CryptographicBuffer.ConvertStringToBinary($"E{pass}", BinaryStringEncoding.Utf8);

            await txCharac.WriteValueAsync(buffer);

            return tcs.Task;
        }

        private static async Task Flash(GattCharacteristic txCharac)
        {
            var tcs = new TaskCompletionSource<int>();
            var buffer = CryptographicBuffer.ConvertStringToBinary($"3", BinaryStringEncoding.Utf8);

            await txCharac.WriteValueAsync(buffer);

            return tcs.Task;
        }

        private static async Task Buzz(GattCharacteristic txCharac)
        {
            var tcs = new TaskCompletionSource<int>();
            var buffer = CryptographicBuffer.ConvertStringToBinary($"M", BinaryStringEncoding.Utf8);

            await txCharac.WriteValueAsync(buffer);

            return tcs.Task;
        }
    }
}