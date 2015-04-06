using System;
using System.Configuration;
using Amazon.SQS;
using Amazon.SQS.Model;
using AtScale.Core;
using AtScale.Core.Repositories;

namespace AtScale.Worker
{
    class Program
    {
        static void Main(string[] args)
        {
            var imageRequestRepository = new ImageRequestRepository();
            var imageResizer = new ImageResizer();

            using (var sqsClient = new AmazonSQSClient())
            {
                // find the queue for us to chit-chat on
                var queueName = ConfigurationManager.AppSettings["AtScaleWorkerQueueName"];
                var queue = sqsClient.CreateQueue(queueName);

                // keep watching the queue
                while (true)
                {
                    // long poll http://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-long-polling.html
                    Console.WriteLine("Checking for messages on the queue...");
                    var receiveResponse = sqsClient.ReceiveMessage(new ReceiveMessageRequest
                    {
                        QueueUrl = queue.QueueUrl,
                        WaitTimeSeconds = 10
                    });

                    foreach (var message in receiveResponse.Messages)
                    {
                        var imageId = message.Body;

                        Console.Write("Resizing {0}... ", imageId);

                        // for our own sake, note that if this worker crashes then the value in the DB
                        // will not be correct. But the SQS message will still hang around, so we'll shortly finish processing it.
                        imageRequestRepository.UpdateStatus(imageId, ResizeStatus.Resizing);
                        var finalUrl = imageResizer.Resize(imageId);
                        imageRequestRepository.UpdatedCompletedStatus(imageId, finalUrl);

                        Console.WriteLine("done!");

                        // Delete from SQS
                        sqsClient.DeleteMessage(new DeleteMessageRequest
                        {
                            QueueUrl = queue.QueueUrl,
                            ReceiptHandle = message.ReceiptHandle
                        });
                    }
                }
            }
        }
    }
}
