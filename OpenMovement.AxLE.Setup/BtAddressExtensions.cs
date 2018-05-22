using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenMovement.AxLE.Setup
{
    static class BtAddressExtensions
    {
        public static ulong ParseBtAddress(this string address)
        {
            address = address.Replace(":", "").Replace("-", "").ToUpper();    // remove address separators
            if (address.Length != 12)
            {
                throw new FormatException("MAC address must be 12 nibbles.");
            }
            return ulong.Parse(address, System.Globalization.NumberStyles.HexNumber);
        }

        public static string FormatBtAddress(this ulong address)
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
    }
}
