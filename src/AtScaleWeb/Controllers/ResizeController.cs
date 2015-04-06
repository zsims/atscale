using System;
using System.Web.Http;
using AtScale.Core;
using AtScale.Core.Repositories;
using AtScale.Web.Models;
using AtScale.Web.Services;

namespace AtScale.Web.Controllers
{
    [RoutePrefix("api")]
    public class ResizeController : ApiController
    {
        private readonly IImageRequestRepository _imageRequestRepository;
        private readonly IWorkerClient _workerClient;
        private readonly IImageRepository _imageRepository;

        public ResizeController()
            : this(new WorkerClient(), new ImageRepository(), new ImageRequestRepository())
        {
        }

        public ResizeController(IWorkerClient workerClient, IImageRepository imageRepository, IImageRequestRepository imageRequestRepository)
        {
            _workerClient = workerClient;
            _imageRepository = imageRepository;
            _imageRequestRepository = imageRequestRepository;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _workerClient.Dispose();
                _imageRequestRepository.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Requests a new resize
        /// </summary>
        /// <returns>An image id to identify the request, and tells the client where to put the image</returns>
        [Route("new-image")]
        [HttpPost]
        public object NewImage(NewImageDetails details)
        {
            var imageId = Guid.NewGuid().ToString();
            var uploadUrl = _imageRepository.GetPublicUploadUrl(imageId, details.MimeType);

            // track the image through our system
            _imageRequestRepository.TrackImage(imageId, uploadUrl, ResizeStatus.New);

            var resizeUrl = new Uri(Request.RequestUri, string.Format("/api/resize/{0}", imageId));

            return new
            {
                UploadEndpoint = uploadUrl,
                ResizeEndpoint = resizeUrl.ToString()
            };
        }

        /// <summary>
        /// Requests resize of a given image
        /// </summary>
        /// <param name="imageId"></param>
        [Route("resize/{imageId}")]
        [HttpPost]
        public object Resize(string imageId)
        {
            // get a worker to do something
            _workerClient.RequestImageResize(imageId);

            var statusUrl = new Uri(Request.RequestUri, string.Format("/api/status/{0}", imageId));

            return new
            {
                StatusEndpoint = statusUrl.ToString()
            };
        }

        /// <summary>
        /// Requests resize of a given image
        /// </summary>
        /// <param name="imageId"></param>
        [Route("status/{imageId}")]
        [HttpGet]
        public object GetStatus(string imageId)
        {
            var status = _imageRequestRepository.GetStatus(imageId);
            return new
            {
                ResizeStatus = status.Status,
                FinalUrl = status.FinalUrl
            };
        }
    }
}
