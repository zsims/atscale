using System;
using System.Configuration;
using System.IO;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace AtScale.Core.Repositories
{
    public interface IImageRepository
    {
        string GetPublicUploadUrl(string imageId, string mimeType);
        string DownloadInputImage(string imageId);
        string UploadOutputImage(string imageId, string imagePath);
    }

    /// <summary>
    /// Stores/retrieves image data
    /// </summary>
    public class ImageRepository : IImageRepository
    {
        private readonly string _bucketName = ConfigurationManager.AppSettings["AtScaleImageBucketName"];

        public string GetPublicUploadUrl(string imageId, string mimeType)
        {
            // get a new S3 endpoint
            using (var s3Client = new AmazonS3Client())
            {
                var uploadUrl = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
                {
                    Key = "input/" + imageId,
                    BucketName = _bucketName,
                    Expires = DateTime.Now.AddHours(1),
                    Verb = HttpVerb.PUT,
                    Protocol = Protocol.HTTP,

                    // Note that the .NET SDK doesn't support multipart uploads: http://stackoverflow.com/questions/20847196/amazon-s3-multipart-upload-using-query-string-authentication-and-net-sdk
                    ContentType = mimeType
                });

                return uploadUrl;
            }
        }

        /// <summary>
        /// Downloads an input image from S3 with the given imageId
        /// </summary>
        /// <param name="imageId"></param>
        /// <returns>A path to the local image</returns>
        public string DownloadInputImage(string imageId)
        {
            var targetFile = Path.GetTempFileName();

            using(var s3Client = new AmazonS3Client())
            {
                using (var response = s3Client.GetObject(new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = "input/" + imageId,
                }))
                {
                    using (var file = new FileStream(targetFile, FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        response.ResponseStream.CopyTo(file);
                    }
                }
            }

            return targetFile;
        }

        public string UploadOutputImage(string imageId, string imagePath)
        {
            using (var s3Client = new AmazonS3Client())
            {
                var key = "output/" + imageId;
                s3Client.PutObject(new PutObjectRequest
                {
                    AutoCloseStream = true,
                    BucketName = _bucketName,
                    Key = key,
                    InputStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read),
                });

                return string.Format("http://s3-{0}.amazonaws.com/{1}/{2}", AWSConfigs.AWSRegion, _bucketName, key);
            }
        }
    }
}
