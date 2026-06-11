using System.Web;
using System.Web.Mvc;

namespace pjtSPEF
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
            // Toda la app exige sesión; las excepciones se marcan con [AllowAnonymous].
            filters.Add(new AuthorizeAttribute());
        }
    }
}
