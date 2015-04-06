namespace AtScale.Core
{
    public enum ResizeStatus
    {
        New,
        Resizing,
        Done
    }

    public class ImageStatus
    {
        public ResizeStatus Status { get; set; }
        public string FinalUrl { get; set; }
    }
}
