namespace pjtSPEF.Models.ViewModels
{
    public class BreadcrumbItem
    {
        public string Texto { get; set; }
        public string Url { get; set; }

        public BreadcrumbItem() { }

        public BreadcrumbItem(string texto, string url = null)
        {
            Texto = texto;
            Url = url;
        }
    }
}
