namespace Stream_Service.Models
{
    public class Station
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Gradient { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }
}
