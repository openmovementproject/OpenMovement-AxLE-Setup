using OpenMovement.AxLE.Comms;
using OpenMovement.AxLE.Comms.Bluetooth.Interfaces;
using OpenMovement.AxLE.Comms.Bluetooth.Windows;
using OpenMovement.AxLE.Comms.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenMovement.AxLE.Setup
{
    class Program
    {
        const string LabelTapeWidth = "9"; // "3.5", "6", "9", "24"
        const string LabelExecutable = @"LabelPrint\PrintLabel.cmd";
        const string LabelArgs = @"Label-AxLE-{tapeWidth}mm.lbx objName {address} objBarcode {addressPlain}";

        IBluetoothManager _bluetoothManager;
        IAxLEManager _axLEManager;

        volatile Queue<string> devices = new Queue<string>();
        private DateTime started;
        private ISet<string> whitelist = new HashSet<string>();
        private ISet<string> reported = new HashSet<string>();
        private int totalFound = 0;
        private int uniqueFound = 0;
        private int axleDevicesFound = 0;

        private void AxLEManager_DeviceFound(object sender, string serial)
        {
            this.totalFound++;
            if (reported.Contains(serial)) { return; }  // ignore duplicate
            reported.Add(serial);   // Add to ignore in future
            this.uniqueFound++;

            this.axleDevicesFound++;
            if (whitelist.Count > 0 && !whitelist.Contains(serial))
            {
                Console.WriteLine("SCAN: Ignoring device not in whitelist: axLE-Band " + " <" + serial.FormatMacAddress() + ">");
                return;
            }

            devices.Enqueue(serial);
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
                Console.WriteLine($"AxLE FOUND: {address.FormatMacAddress()}");
                Console.WriteLine("Connecting...");

                Comms.AxLE device;
                try
                {
                    device = await _axLEManager.ConnectDevice(address);
                }
                catch (Exception e)
                {
                    Console.WriteLine("ERROR: Problem connecting to device" + address.FormatMacAddress() + ": " + e.Message);
                    devices.Enqueue(address);   // retry later
                    return false;
                }

                Console.WriteLine($"Serial: {address} <{address.FormatMacAddress()}>");

                var pass = address.Substring(address.Length - 6);
                Console.WriteLine("Attempting Auth...");

                if (!await device.Authenticate(pass))
                {
                    Console.WriteLine("AUTH FAILED!! Device must be wiped to continue proceed? (Y/N)");
                    if (Console.ReadKey().Key == ConsoleKey.Y)
                    {
                        Console.WriteLine("Resetting device...");
                        await device.ResetPassword();
                        Thread.Sleep(500);
                        Console.WriteLine("Device reset. Authenticating...");
                        await device.Authenticate(pass);
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
                    await device.LEDFlash();
                    await device.VibrateDevice();
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
                            var macAddress = address.FormatMacAddress();
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

            _bluetoothManager = new BluetoothManager();
            _axLEManager = new AxLEManager(_bluetoothManager)
            {
                RssiFilter = 40 // Scan only nearby AxLEs
            };
            _axLEManager.DeviceFound += AxLEManager_DeviceFound;

            // Start the scanner
            try
            {
                await _axLEManager.StartScan();
            }
            catch (Exception e)
            {
                Console.WriteLine("ERROR: Problem starting scanner: " + e.Message);
                return;
            }

            try
            {
                while (!await MainTasks()) {}
            }
            finally
            {
                await _axLEManager.StopScan();
            }
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