namespace glFTPd_Commander.Models
{
    public class IpRestriction(string ipKey, string ipValue)
    {
        public string IpKey { get; set; } = ipKey;
        public string IpValue { get; set; } = ipValue;
    }
}
