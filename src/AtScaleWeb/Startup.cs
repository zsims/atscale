using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Owin;
using System.Web.Http;

namespace AtScale.Web
{
    public class Startup
    {
        public void Configuration(IAppBuilder appBuilder)
        {
            var config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();

            // Use JSON over XML for the API if not specified
            {
                config.Formatters.XmlFormatter.SupportedMediaTypes.Clear();
                var jsonFormatter = config.Formatters.JsonFormatter;
                var settings = jsonFormatter.SerializerSettings;
                settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                settings.Formatting = Formatting.Indented;
                settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            }

            appBuilder.UseWebApi(config);
        } 
    }
}
