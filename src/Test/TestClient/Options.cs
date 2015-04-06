using CommandLine;
using CommandLine.Text;

namespace TestClient
{
    public class Options
    {
        [Option('i', "input", HelpText = "Path to an image to upload and resize", Required = true)]
        public string InputImage { get; set; }

        [Option('o', "output", HelpText = "Where to write the final image", Required = true)]
        public string OutputImage { get; set; }

        [Option('e', "endpoint", HelpText = "Endpoint of the service to use, e.g. http://atscale.com/", Required = true)]
        public string Endpoint { get; set; }

        [HelpOption]
        public string GetHelp()
        {
            return HelpText.AutoBuild(this, current => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }
}
