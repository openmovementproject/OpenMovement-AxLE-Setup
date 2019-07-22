using System;


namespace OpenMovement.AxLE.Setup
{
    public class BitmapException : Exception
    {
        public BitmapException(string message) : base(message) {; }
    }

    // Represents an RGBA8888 pixel
    public struct Pixel
    {
        public readonly byte R, G, B, A;

        // Create from component 8-bit values
        public Pixel(byte r, byte g, byte b, byte a)
        {
            this.R = r;
            this.G = g;
            this.B = b;
            this.A = a;
        }

        // Create from a buffer and offset in BGRA8888 order
        public static Pixel FromBgra(byte[] buffer, uint offset)
        {
            return new Pixel(buffer[offset + 2], buffer[offset + 1], buffer[offset + 0], buffer[offset + 3]);
        }

        // Create from a buffer and offset in BGR888 order
        public static Pixel FromBgr(byte[] buffer, uint offset)
        {
            return new Pixel(buffer[offset + 2], buffer[offset + 1], buffer[offset + 0], 0);
        }

        // Simple 1-RGBA components
        public Pixel Negate
        {
            get
            {
                return new Pixel((byte)(255 - R), (byte)(255 - G), (byte)(255 - B), (byte)(255 - A));
            }
        }

        // CIE 1931 linear luminance (sRGB)
        public double Luminance
        {
            get
            {
                return 0.2126 * (R / 255.0) + 0.7152 * (G / 255.0) + 0.0722 * (B / 255.0);
            }
        }

        // true=light, false=dark
        public bool Monochrome
        {
            get
            {
                return Luminance >= 0.5;
            }
        }

        public static readonly Pixel BLACK = new Pixel(0, 0, 0, 0);
    }

    public class Bitmap
    {
        // Core bitmap information
        private readonly byte[] buffer;             // Data buffer (typically whole file)
        private readonly uint width;                // Number of cols
        private readonly uint height;               // Number of rows
        private readonly uint bitsPerPixel;         // Bits-per-pixel (1/4/8/24/32 valid)
        private readonly int stride;                // Stride offset for next row down (signed, as bitmaps typically bottom-up)
        private readonly uint dataOffset;           // Offset of top-left pixel (bitmaps typically bottom-up, so usually last row in bitmap)
        private readonly uint paletteOffset;        // Palette stored in BGRX format

        // View modified by cropping, rotation and inversion
        private readonly int offsetX;               // X-axis offset
        private readonly int offsetY;               // Y-axis offset
        private readonly bool swapAxes;             // Axes swapped
        private readonly bool invertX;              // X-axis inverted
        private readonly bool invertY;              // Y-axis inverted
        private readonly bool negative;             // Negate image

        // Full constructor
        public Bitmap(byte[] buffer, uint width, uint height, uint bitsPerPixel = 32, int stride = 0, uint dataOffset = 0, uint paletteOffset = 0, 
            int offsetX = 0, int offsetY = 0, bool swapAxes = false, bool invertX = false, bool invertY = false, bool negative = false)
        {
            this.buffer = buffer;
            this.width = width;
            this.height = height;
            this.bitsPerPixel = bitsPerPixel;
            this.stride = stride == 0 ? (int)(((width * bitsPerPixel + 31) / 32) * 4) : stride;
            this.dataOffset = dataOffset;
            this.paletteOffset = paletteOffset;
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            this.swapAxes = swapAxes;
            this.invertX = invertX;
            this.invertY = invertY;
            this.negative = negative;
        }

        // Constructor for a modified view of a given bitmap
        private Bitmap(Bitmap bitmap, bool swapAxes, bool invertX, bool invertY, bool negative)
            : this(bitmap.buffer, bitmap.width, bitmap.height, bitmap.bitsPerPixel, bitmap.stride, bitmap.dataOffset, bitmap.paletteOffset, 
                  bitmap.offsetX, bitmap.offsetY, swapAxes, invertX, invertY, negative)
        {
        }

        // The apparent width (after view changes)
        public uint Width { get { return swapAxes ? height : width; } }

        // The apparent height (after view changes)
        public uint Height { get { return swapAxes ? width : height; } }

        // Creates a new Bitmap from .BMP data, taking ownership of the given byte array.
        public static Bitmap FromBitmapData(byte[] buffer)
        {
            if (buffer.Length < 54 || buffer[0] != 'B' || buffer[1] != 'M') throw new BitmapException("Unsupported BMP format: invalid header");
            uint paletteOffset = BitConverter.ToUInt32(buffer, 0x0E) + 0x0E;
            uint width = BitConverter.ToUInt32(buffer, 0x12);
            int pixelHeight = BitConverter.ToInt32(buffer, 0x16);       // negative for top-down
            uint height = (uint)Math.Abs(pixelHeight);
            uint bitsPerPixel = BitConverter.ToUInt16(buffer, 0x1C);
            uint compression = BitConverter.ToUInt16(buffer, 0x1E);
            if (compression != 0) throw new BitmapException("Unsupported BMP format: compression");
            uint dataOffset = BitConverter.ToUInt32(buffer, 0x0A);
            int stride = (int)((pixelHeight < 0 ? 1 : -1) * (int)(((width * bitsPerPixel + 31) / 32) * 4));
            if (pixelHeight > 0) dataOffset += (uint)((pixelHeight - 1) * -stride);
            if (bitsPerPixel != 1 && bitsPerPixel != 4 && bitsPerPixel != 8 && bitsPerPixel != 24 && bitsPerPixel != 32) throw new BitmapException("Unsupported BMP format: bits-per-pixel");
            return new Bitmap(buffer, width, height, bitsPerPixel, stride, dataOffset, paletteOffset);
        }

        // Creates a new Bitmap from .BMP file.
        public static Bitmap FromBitmapFile(string filename)
        {
            byte[] data = System.IO.File.ReadAllBytes(filename);
            return FromBitmapData(data);
        }

        // Returns the pixel from the given coordinates in the apparent view of the bitmap
        public Pixel PixelAt(int x, int y)
        {
            if (swapAxes) { (x, y) = (y, x); }
            if (invertX) x = (int)(this.width - 1 - x);
            if (invertY) y = (int)(this.height - 1 - y);
            x += offsetX;
            y += offsetY;
            Pixel? pixel = null;
            if (x >= 0 && y >= 0 && x - offsetX < this.width && y - offsetY < this.height)
            {
                uint rowStart = (uint)(this.dataOffset + y * this.stride);
                switch (this.bitsPerPixel)
                {
                    case 1:     // 1-bit: left-most as MSB, right-most as LSB. Palette value.
                        {
                            uint offset = (uint)(rowStart + (x / 8));
                            byte data = this.buffer[offset];
                            uint value = (uint)((data & (1 << (7 - (x & 7)))) != 0 ? 1 : 0);
                            pixel = Pixel.FromBgra(this.buffer, this.paletteOffset + value * 4);
                            break;
                        }
                    case 4:     // 4-bit: left value in high nibble, right value in low nibble. Palette value.
                        {
                            uint offset = (uint)(rowStart + (x / 2));
                            byte data = this.buffer[offset];
                            uint value = (uint)(((x & 1) != 0) ? (data & 0x0f) : (data >> 4));
                            pixel = Pixel.FromBgra(this.buffer, this.paletteOffset + value * 4);
                            break;
                        }
                    case 8:     // 8-bit: Palette value.
                        {
                            uint offset = (uint)(rowStart + x);
                            uint value = this.buffer[offset];
                            pixel = Pixel.FromBgra(this.buffer, this.paletteOffset + value * 4);
                            break;
                        }
                    case 24:    // 24-bit: BGR888
                        {
                            uint offset = (uint)(rowStart + (x * 3));
                            pixel = Pixel.FromBgr(this.buffer, offset);
                            break;
                        }
                    case 32:    // 32-bit BGRA8888
                        {
                            uint offset = (uint)(rowStart + (x * 4));
                            pixel = Pixel.FromBgra(this.buffer, offset);
                            break;
                        }
                }
            }
            if (!pixel.HasValue) return Pixel.BLACK;
            return this.negative ? pixel.Value.Negate : pixel.Value;
        }

        // Returns a bitmap with a rotated view of this one
        public Bitmap Rotate(int degrees)
        {
            // Proper handling of negative modulo
            degrees = degrees - (((degrees + (degrees < 0 ? -359 : 0)) / 360) * 360);            
            if (degrees >= 45 && degrees < 135) // 90
            {
                return new Bitmap(this, !this.swapAxes, this.invertX, !this.invertY, this.negative);
            }
            else if (degrees >= 135 && degrees < 225) // 180
            {
                return new Bitmap(this, this.swapAxes, !this.invertX, !this.invertY, this.negative);
            }
            else if (degrees >= 225 && degrees < 315) // 270
            {
                return new Bitmap(this, !this.swapAxes, !this.invertX, this.invertY, this.negative);
            }
            else // 0
            {
                return new Bitmap(this, this.swapAxes, this.invertX, this.invertY, this.negative);
            }
        }

        // Returns a bitmap with a horizontally-flipped view of this one
        public Bitmap FlipHorizontal()
        {
            return new Bitmap(this, this.swapAxes, !this.invertX, this.invertY, this.negative);
        }

        // Returns a bitmap with a vertically-flipped view of this one
        public Bitmap FlipVertical()
        {
            return new Bitmap(this, this.swapAxes, this.invertX, !this.invertY, this.negative);
        }

        // Returns a bitmap with a negated view of this one
        public Bitmap Negate()
        {
            return new Bitmap(this, this.swapAxes, this.invertX, this.invertY, !this.negative);
        }

        // Returns a bitmap with a cropped view of this one
        public Bitmap Crop(int cropX, int cropY, int cropW, int cropH)
        {
            if (this.swapAxes) { (cropX, cropY) = (cropY, cropX); (cropW, cropH) = (cropH, cropW); }
            int ox = this.offsetX + (this.invertX ? -1 : 1) * cropX;
            int oy = this.offsetY + (this.invertY ? -1 : 1) * cropY;
            return new Bitmap(this.buffer, (uint)cropW, (uint)cropH,
                this.bitsPerPixel, this.stride, this.dataOffset, this.paletteOffset,
                ox, oy,
                this.swapAxes, this.invertX, this.invertY, this.negative);
        }


        // Pack as 1-bit 8-pixel row; (Width) from image; rows as (data.Length / Width)
        public byte[] PackMonochrome()
        {
            uint rows = (Height + 7) / 8;
            byte[] data = new byte[rows * Width];
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    bool value = PixelAt(x, y).Monochrome;
                    if (value) data[(y >> 3) * Width + x] |= (byte)(1 << (y & 7));
                    else data[(y >> 3) * Width + x] &= (byte)~(1 << (y & 7));
                }
            }
            return data;
        }

        // For debugging purposes only: create a multi-line string representation of a monochrome image
        public string DebugDumpString()
        {
            var sb = new System.Text.StringBuilder();
            for (var y = 0; y < Height; y += 2)
            {
                for (var x = 0; x < Width; x++)
                {
                    bool upper = PixelAt(x, y).Monochrome;
                    bool lower = y + 1 < Height ? PixelAt(x, y + 1).Monochrome : false;
                    char c;
                    if (upper && lower) c = '\u2588';
                    else if (upper) c = '\u2580';
                    else if (lower) c = '\u2584';
                    else c = ' ';
                    sb.Append(c);
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }
    }


}
