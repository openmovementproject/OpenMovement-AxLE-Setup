using OpenMovement.AxLE.Comms;
using OpenMovement.AxLE.Comms.Bluetooth.Interfaces;
using OpenMovement.AxLE.Comms.Bluetooth.Windows;
using OpenMovement.AxLE.Comms.Interfaces;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenMovement.AxLE.Setup
{
    class Program
    {
        //const string imageFile = @"..\..\_local\toucan.bmp";
        const int IMAGE_SIZE = 32;

        string filterString = "";     // D/2CB$ T/BDB$
        string imageFile = null;
        string imageBackground = null;
        int imageOffsetX = 0;
        int imageOffsetY = 0;
        bool imageNegate = false;
        int imageRotate = -90;
        bool imageFlipH = false;
        bool imageFlipV = false;
        int imageStart = 64;
        int imageHeight = 0;

        bool imageTest = false;

        bool autoImage = false;
        bool autoExit = false;

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

        Bitmap GetBitmap()
        {
            Console.WriteLine($"Loading Image... {imageFile}");
            var bitmap = Bitmap.FromBitmapFile(imageFile);
            Console.WriteLine("~~~ Original ~~~"); Console.Write(bitmap.DebugDumpString());
            if (imageOffsetX != 0 || imageOffsetY != 0) { Console.WriteLine("~~~ Crop ~~~"); bitmap = bitmap.Crop(imageOffsetX, imageOffsetY, IMAGE_SIZE, IMAGE_SIZE); Console.Write(bitmap.DebugDumpString()); }
            if (imageFlipH) { Console.WriteLine($"~~~ Flip-H ~~~"); bitmap = bitmap.FlipHorizontal(); Console.Write(bitmap.DebugDumpString()); }
            if (imageFlipV) { Console.WriteLine($"~~~ Flip-V ~~~"); bitmap = bitmap.FlipVertical(); Console.Write(bitmap.DebugDumpString()); }

            if (imageBackground != null)
            {
                Bitmap bitmapBackground = Bitmap.FromBitmapFile(imageBackground);
                Console.WriteLine("~~~ Background ~~~"); Console.Write(bitmapBackground.DebugDumpString());
                if (imageFlipH) { bitmapBackground = bitmapBackground.FlipHorizontal(); }
                if (imageFlipV) { bitmapBackground = bitmapBackground.FlipVertical(); }

                Console.WriteLine("~~~ Height Over-Crop with background ~~~");
                bitmap = bitmap.Crop(0, 0, (int)bitmapBackground.Width, (int)bitmapBackground.Height);
                bitmap = bitmap.WithBackground(bitmapBackground);
                Console.Write(bitmap.DebugDumpString());
            }

            if (imageNegate) { Console.WriteLine("~~~ Negate ~~~"); bitmap = bitmap.Negate(); Console.Write(bitmap.DebugDumpString()); }
            if (imageRotate != 0) { Console.WriteLine($"~~~ Rotate {imageRotate} ~~~"); bitmap = bitmap.Rotate(imageRotate); Console.Write(bitmap.DebugDumpString()); }
            return bitmap;
        }

        byte[] GetBitmapData(Bitmap bitmap)
        {
            var data = bitmap.PackMonochrome();
            for (var i = 0; i < data.Length; i++) { Console.Write($"0x{data[i]:X2}, "); if ((i & 15) == 15 || i + 1 >= data.Length) Console.WriteLine(); }
            return data;
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

                Regex filter = null;
                if (filterString != null && filterString.Length > 0) filter = new Regex(filterString, RegexOptions.IgnoreCase);

                if (filter != null && filter.Match(address).Length <= 0)
                {
                    Console.WriteLine($"DEVICE: Ignoring as not matched filter: {address}");

                }
                else
                {
                    Console.WriteLine($"DEVICE: Connecting... {address.FormatMacAddress()}");

                    IAxLE device;
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

                    Console.WriteLine("*** Flashing and buzzing device ***");
                    for (; ; )
                    {
                        await device.LEDFlash();
                        await device.VibrateDevice();
                        if (Console.KeyAvailable || autoImage)
                        {
                            Console.Write("Found. P=Print, I=Image+Time, N=Skip (P/I/N) >");
                            var key = autoImage ? ConsoleKey.I : Console.ReadKey().Key;
                            if (key == ConsoleKey.N)
                            {
                                Console.WriteLine("Skipping...");
                                break;
                            }
                            else if (key == ConsoleKey.P)
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
                            else if (key == ConsoleKey.I)
                            {
                                Console.WriteLine($"Setting time...");
                                await device.WriteRealTime(DateTime.Now);

                                if (imageFile != null)
                                {
                                    Bitmap bitmap = GetBitmap();
                                    var data = GetBitmapData(bitmap);
                                    await device.WriteBitmap(data, 0);
                                    await device.DisplayIcon(0, imageStart, imageHeight > 0 ? imageHeight : (int)bitmap.Width);    // Image width is height on screen
                                    Console.WriteLine($"...done.");
                                }

                                if (autoExit)
                                {
                                    return true;
                                }
                                break;
                            }

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

            if (imageTest)
            {
                Bitmap bitmap = GetBitmap();
                GetBitmapData(bitmap);
                Console.WriteLine("DEBUG: Stopped after image test - press ENTER to exit...");
                Console.In.Read();
                return;
            }

            _bluetoothManager = new BluetoothManager();
            _axLEManager = new AxLEManager(_bluetoothManager)
            {
                RssiFilter = 0 // 40 Scan only nearby AxLEs
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

        public bool UseArgs(string[] args)
        {
            bool help = false;
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "/h" || arg == "/?" || arg == "-h" || arg == "-?" || arg == "-help" || arg == "--help") { help = true; }
                else if (arg == "-device") { filterString = args[++i]; }
                else if (arg == "-image") { imageFile = args[++i]; }
                else if (arg == "-image-background") { imageBackground = args[++i]; }
                else if (arg == "-image-neg") { imageNegate = true; }
                else if (arg == "-image-rot") { imageRotate = int.Parse(args[++i]); }
                else if (arg == "-image-fliph") { imageFlipH = true; }
                else if (arg == "-image-flipv") { imageFlipV = true; }
                else if (arg == "-image-offx") { imageOffsetX = int.Parse(args[++i]); }
                else if (arg == "-image-offy") { imageOffsetY = int.Parse(args[++i]); }
                else if (arg == "-image-height") { imageHeight = int.Parse(args[++i]); }
                else if (arg == "-image-start") { imageStart = int.Parse(args[++i]); }
                else if (arg == "-image-test") { imageTest = true; }
                else if (arg == "-auto-image") { autoImage = true; }
                else if (arg == "-auto-exit") { autoExit = true; }
                else
                {
                    Console.Error.WriteLine("ERROR: Unknown parameter: " + arg);
                    return false;
                }
            }

            if (help)
            {
                Console.WriteLine("Usage: OpenMovement.AxLE.Setup [-device <device_pattern>] [-auto] [-image <filename.bmp>] [-image-background <background.bmp>] [-image-neg] [-image-rot <degrees=-90>] [-image-flip{h|v}] [-image-off{x|y} <offset>] [-image-height <rows=32>] [-image-start <row=64>]\n");
                Console.WriteLine("\n");
                Console.WriteLine("Where: 'device_pattern' is a regular expression to match against the device address (caps, no colons), e.g. \"2CB$\" will match any address: ??:??:??:??:?2:CB\n");
                Console.WriteLine("       '-auto-image' automatically sends the image (if provided) and sets the time on any matching device\n");
                Console.WriteLine("       '-auto-exit' automatically stops after one device\n");
                Console.WriteLine("\n");
                return false;
            }

            return true;
        }

        static void Main(string[] args)
        {
            Program program = new Program();
            if (program.UseArgs(args))
            {
                Task.Run(program.Run).Wait();
            }
#if DEBUG
            Console.WriteLine("END: Press any key to continue...");
            Console.ReadKey();
#endif
        }


    }
}
