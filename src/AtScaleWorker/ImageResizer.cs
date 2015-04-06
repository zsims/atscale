using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using AtScale.Core.Repositories;

namespace AtScale.Worker
{
    public class ImageResizer
    {
        private readonly ImageRepository _imageRepository = new ImageRepository();

        /// <summary>
        /// Resizes the image
        /// </summary>
        /// <param name="imageId">Image id of the image</param>
        /// <returns>S3 URL where the image is stored</returns>
        public string Resize(string imageId)
        {
            // download it somewhere
            var localFile = _imageRepository.DownloadInputImage(imageId);

            // find somewhere for the new image to go
            var resizedFile = Path.GetTempFileName();

            // resize it
            ResizeImageFile(localFile, resizedFile);

            // put it back in S3
            return _imageRepository.UploadOutputImage(imageId, resizedFile);
        }

        private static void ResizeImageFile(string localFile, string resizedFile)
        {
            using (var image = Image.FromFile(localFile))
            {
                var newWidth = image.Width/2;
                var newHeight = image.Height/2;

                using (var newImage = new Bitmap(image.Width/2, image.Height/2))
                {
                    using (var graphicsHandle = Graphics.FromImage(newImage))
                    {
                        graphicsHandle.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphicsHandle.DrawImage(image, 0, 0, newWidth, newHeight);
                    }

                    newImage.Save(resizedFile, image.RawFormat);
                }
            }
        }
    }
}
