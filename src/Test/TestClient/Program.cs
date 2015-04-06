using System;
using System.IO;
using System.Net;
using System.Web;
using RestSharp;
using System.Threading;
using TestClient.Models;

namespace TestClient
{
    class Program
    {
        private static void Main(string[] args)
        {
            var options = new Options();
            if (!CommandLine.Parser.Default.ParseArguments(args, options)) return;

            // Find out the mime type
            var mimeType = MimeMapping.GetMimeMapping(Path.GetExtension(options.InputImage));
            Console.WriteLine("Detected MIME type {0}", mimeType);

            // 1. Get a new image id
            Console.Write("Getting an image id... ");
            var imageIdResponse = GetImageId(options, mimeType);
            Console.WriteLine("done!");

            // 2. Upload the image
            var uploadEndpoint = imageIdResponse.UploadEndpoint;
            Console.Write("Uploading... ");
            UploadImage(uploadEndpoint, mimeType, options.InputImage);
            Console.WriteLine("done!");

            // 3. Kick off the resize
            Console.Write("Requesting resize... ");
            var resizeResponse = RequestResize(imageIdResponse.ResizeEndpoint);
            Console.WriteLine("done!");

            // 4. Wait for it to be resized
            StatusResponse status;
            while (true)
            {
                Console.Write("Checking status... ");
                status = GetStatus(resizeResponse.StatusEndpoint);
                Console.WriteLine("{0}", status.ResizeStatus);
                if (status.ResizeStatus == "Done")
                {
                    break;
                }
                Thread.Sleep(5000);
            }

            // 5. Download it!
            Console.Write("Downloading to {0}... ", options.OutputImage);
            DownloadFinalImage(options.OutputImage, status.FinalUrl);
            Console.WriteLine("done!");

            Console.ReadLine();
        }

        private static void DownloadFinalImage(string outputImage, string finalUrl)
        {
            using (var wc = new WebClient())
            {
                wc.DownloadFile(finalUrl, outputImage);
            }
        }

        private static StatusResponse GetStatus(string statusEndpoint)
        {
            var client = new RestClient(statusEndpoint);
            var request = new RestRequest();
            var response = client.Execute<StatusResponse>(request);

            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }

            return response.Data;
        }

        private static ResizeResponse RequestResize(string resizeEndpoint)
        {
            var client = new RestClient(resizeEndpoint);
            var request = new RestRequest(Method.POST);
            var response = client.Execute<ResizeResponse>(request);

            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }

            return response.Data;
        }

        private static void UploadImage(string uploadEndpoint, string mimeType, string inputImage)
        {
            // It's a royal PITA to get RestSharp to PUT binary w/o multi-part
            using (var wc = new WebClient())
            {
                // Note that the content type is built into the S3 signature, so it must be the same on both ends
                wc.Headers.Add("Content-Type", mimeType);

                wc.UploadFile(uploadEndpoint, "PUT", inputImage);
            }
        }

        private static NewImage GetImageId(Options options, string mimeType)
        {
            var client = new RestClient(options.Endpoint);
            var request = new RestRequest("/api/new-image", Method.POST)
            {
                RequestFormat = DataFormat.Json
            };

            request.AddBody(new
            {
                @MimeType = mimeType
            });

            var response = client.Execute<NewImage>(request);

            if (response.ErrorException != null)
            {
                throw response.ErrorException;
            }

            return response.Data;
        }
    }
}
