using System;
using System.Configuration;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace AtScale.Web.Services
{
    public interface IWorkerClient : IDisposable
    {
        void RequestImageResize(string imageId);
    }

    /// <summary>
    /// Client for communicating with workers
    /// </summary>
public class WorkerClient : IWorkerClient
{
    private AmazonSQSClient _sqsClient;
    private readonly string _queueUrl;

    public WorkerClient()
    {
        _sqsClient = new AmazonSQSClient();
        var queueName = ConfigurationManager.AppSettings["AtScaleWorkerQueueName"];

        // create or find the queue for us to chit-chat on
        _queueUrl = _sqsClient.CreateQueue(queueName).QueueUrl;
    }

    public void RequestImageResize(string imageId)
    {
        var request = new SendMessageRequest
        {
            MessageBody = imageId,
            QueueUrl = _queueUrl
        };

        _sqsClient.SendMessage(request);
    }

    public void Dispose()
    {
        if (_sqsClient != null)
        {
            _sqsClient.Dispose();
            _sqsClient = null;
        }
    }
}
}
