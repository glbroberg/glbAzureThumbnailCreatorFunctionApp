using System;
using System.IO;
using System.Threading.Tasks;
using LocalImageFunctionApp.Models;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LocalImageFunctionApp
{
    public static class Function1
    {

        [FunctionName("Function1")]
        public static async Task Run(
            [ServiceBusTrigger("localimagequeue", Connection = "ServiceBusConnection")] string myQueueItem,
            MessageReceiver messageReceiver,
            ExecutionContext context,

            string lockToken,
            ILogger log)
        {

                var config = new ConfigurationBuilder()
                       .SetBasePath(context.FunctionAppDirectory)
                       .AddJsonFile("local.settings.json", true, true)
                       .AddEnvironmentVariables().Build();
                var key = config["CloudStorageAccount"];


            log.LogInformation($"Success");



            log.LogInformation($"Image Name: {myQueueItem}");
            var deserializedObject = JsonConvert.DeserializeObject<ImageModel>(myQueueItem);
            await CreateImages(config, deserializedObject.ContainerName, deserializedObject.ImageName);
            //log.LogInformation($"Image Name: {myQueueItem.ImageName}  Container Name: {myQueueItem.ContainerName}");
            await messageReceiver.CompleteAsync(lockToken);
        }

        private static async Task CreateImages(IConfiguration config, string containerName, string fileName)
        {
            try
            {
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(config["CloudStorageAccount"]);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                CloudBlobContainer container = blobClient.GetContainerReference(containerName);
                CloudBlockBlob cloudBlockBlob = container.GetBlockBlobReference(fileName);

                using var file = new MemoryStream();
                using var thOutput = new MemoryStream();
                using var output = new MemoryStream();


                await cloudBlockBlob.DownloadToStreamAsync(file);
                file.Position = 0;
                var extension = Path.GetExtension(fileName);
                var name = Path.GetFileNameWithoutExtension(fileName);
                Image<Rgba32> image = (Image<Rgba32>)await Image.LoadAsync(file);

                var jpegEncoder = new JpegEncoder
                {
                    Quality = Convert.ToInt32(config["JPEG_LOW_RESOLUTION_QUALITY"]),
                };
                image.Save(output, jpegEncoder);
                output.Position = 0;
                cloudBlockBlob = container.GetBlockBlobReference($"{name}-lr.jpg");
                await cloudBlockBlob.UploadFromStreamAsync(output);

                var thumbnailWidth = Convert.ToInt32(config["JPEG_THUMBNAIL_WIDTH"]);
                var divisor = image.Width / thumbnailWidth;
                var height = Convert.ToInt32(Math.Round((decimal)(image.Height / divisor)));
                image.Mutate(x => x.Resize(thumbnailWidth, height));
                jpegEncoder.Quality = Convert.ToInt32(config["JPEG_THUMBNAIL_QUALITY"]);
                image.Save(thOutput, jpegEncoder);
                thOutput.Position = 0;
                cloudBlockBlob = container.GetBlockBlobReference($"{name}-th.jpg");
                await cloudBlockBlob.UploadFromStreamAsync(thOutput);
            }
            catch(Exception e) 
            {

            }
            
        }
    }
}
