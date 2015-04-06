using System;
using System.Collections.Generic;
using System.Configuration;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace AtScale.Core.Repositories
{
    public interface IImageRequestRepository : IDisposable
    {
        void TrackImage(string imageId, string uploadUrl, ResizeStatus status);
        ImageStatus GetStatus(string imageId);
        void UpdateStatus(string imageId, ResizeStatus status);
        void UpdatedCompletedStatus(string imageId, string finalUrl);
    }

    /// <summary>
    /// Stores image resize requests
    /// </summary>
    public class ImageRequestRepository : IImageRequestRepository
    {
        private AmazonDynamoDBClient _client;
        private readonly string _tableName = ConfigurationManager.AppSettings["AtScaleImageRequestsTableName"];

        public ImageRequestRepository()
        {
            _client = new AmazonDynamoDBClient();
        }

        /// <summary>
        /// Files a new image request
        /// </summary>
        /// <param name="imageId"></param>
        /// <param name="uploadUrl"></param>
        /// <param name="status"></param>
        public void TrackImage(string imageId, string uploadUrl, ResizeStatus status)
        {
            var request = new PutItemRequest
            {
                TableName = _tableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    { "ImageId", new AttributeValue() { S = imageId }},
                    { "ResizeStatus", new AttributeValue() { S = status.ToString() }},
                    { "UploadUrl", new AttributeValue() { S = uploadUrl }}
                }
            };

            _client.PutItem(request);
        }

        /// <summary>
        /// Gets the status of an image id
        /// </summary>
        /// <param name="imageId"></param>
        /// <returns></returns>
        public ImageStatus GetStatus(string imageId)
        {
            var request = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    {"ImageId", new AttributeValue {S = imageId}}
                },
                ProjectionExpression = "ResizeStatus, FinalUrl",
                ConsistentRead = true
            };

            var response = _client.GetItem(request);

            ResizeStatus status;
            Enum.TryParse(response.Item["ResizeStatus"].S, out status);

            var finalUrl = string.Empty;
            if (response.Item.ContainsKey("FinalUrl"))
            {
                finalUrl = response.Item["FinalUrl"].S;
            }
            return new ImageStatus
            {
                Status = status,
                FinalUrl = finalUrl
            };
        }

        public void UpdateStatus(string imageId, ResizeStatus status)
        {
            var request = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    {"ImageId", new AttributeValue { S = imageId }}
                },
                ExpressionAttributeNames = new Dictionary<string, string>()
                {
                    {"#RS", "ResizeStatus"},
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>()
                {
                    {":rs",new AttributeValue {S = status.ToString()}},
                },
                UpdateExpression = "SET #RS = :rs",
            };

            _client.UpdateItem(request);
        }

        public void UpdatedCompletedStatus(string imageId, string finalUrl)
        {
            var request = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    {"ImageId", new AttributeValue { S = imageId }}
                },
                ExpressionAttributeNames = new Dictionary<string, string>()
                {
                    {"#RS", "ResizeStatus"},
                    {"#FU", "FinalUrl"},
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>()
                {
                    {":rs",new AttributeValue { S = ResizeStatus.Done.ToString() }},
                    {":fu",new AttributeValue { S = finalUrl }}
                },
                UpdateExpression = "SET #RS = :rs, #FU = :fu"
            };

            _client.UpdateItem(request);
        }

        public void Dispose()
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
        }
    }
}
