namespace OpenMovement.AxLE.Setup
{
    static class MacAddressExtensions
    {
        public static string FormatMacAddress(this string address)
        {
            return string.Format("{0}:{1}:{2}:{3}:{4}:{5}",
                    address.Substring(0, 2),
                    address.Substring(2, 2),
                    address.Substring(4, 2),
                    address.Substring(6, 2),
                    address.Substring(8, 2),
                    address.Substring(10, 2)
                );
        }
    }
}
