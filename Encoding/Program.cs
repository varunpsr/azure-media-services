using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.MediaServices;
using System.Configuration;
using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;
using System.Threading;
using Microsoft.WindowsAzure.MediaServices.Client.ContentKeyAuthorization;

namespace Encoding
{
    class Program
    {

        private static readonly string MediaServicesAccountName = ConfigurationManager.AppSettings["MediaServicesAccountName"];
        private static readonly string MediaServicesAccountKey = ConfigurationManager.AppSettings["MediaServicesAccountKey"];

        private static readonly Uri SampleIssuer = new Uri(ConfigurationManager.AppSettings["Issuer"]);
        private static readonly Uri SampleAudience = new Uri(ConfigurationManager.AppSettings["Audience"]);

        private static readonly string _externalStorageAccountName = ConfigurationManager.AppSettings["ExternalStorageAccountName"];
        private static readonly string _externalStorageAccountKey = ConfigurationManager.AppSettings["ExternalStorageAccountKey"];

        private static CloudMediaContext _context = null;
        private static MediaServicesCredentials _cachedCredentials = null;

        private static CloudStorageAccount _sourceStorageAccount = null;
        private static CloudStorageAccount _destinationStorageAccount = null;

        private const string SingleMp4File = @"C:\Users\user_name\Desktop\abcd.mp4";

        static void Main(string[] args)
        {
            // Create and cache the Media Services credentials in a static class variable.
            _cachedCredentials = new MediaServicesCredentials(MediaServicesAccountName, MediaServicesAccountKey);

            _context = new CloudMediaContext(_cachedCredentials);

            StorageCredentials mediaServicesStorageCredentials = new StorageCredentials(_externalStorageAccountName, _externalStorageAccountKey);
            _destinationStorageAccount = new CloudStorageAccount(mediaServicesStorageCredentials, false);


            IAsset asset = UploadFileAndCreateAsset(SingleMp4File);
            Console.WriteLine("Uploaded asset: {0}", asset.Id);

            IAsset encodedAsset = EncodeToAdaptiveBitrateMp4Set(asset);
            Console.WriteLine("Encoded asset: {0}", encodedAsset.Id);


            // You can use the http://smf.cloudapp.net/healthmonitor player to test the smoothStreamURL URL.
            string url = GetStreamingOriginLocator(encodedAsset);
            Console.WriteLine("Smooth Streaming URL: {0}/manifest", url);

            Console.ReadLine();
        }

        static public IAsset UploadFileAndCreateAsset(string singleFilePath)
        {
            if (!File.Exists(singleFilePath))
            {
                Console.WriteLine("File does not exist.");
                return null;
            }

            var assetName = Path.GetFileNameWithoutExtension(singleFilePath);
            IAsset inputAsset = _context.Assets.Create(assetName, AssetCreationOptions.None);

            var assetFile = inputAsset.AssetFiles.Create(Path.GetFileName(singleFilePath));

            Console.WriteLine("Created assetFile {0}", assetFile.Name);

            var policy = _context.AccessPolicies.Create(
                                    assetName,
                                    TimeSpan.FromDays(30),
                                    AccessPermissions.Write | AccessPermissions.List);

            var locator = _context.Locators.CreateLocator(LocatorType.Sas, inputAsset, policy);

            Console.WriteLine("Upload {0}", assetFile.Name);

            assetFile.Upload(singleFilePath);
            Console.WriteLine("Done uploading {0}", assetFile.Name);

            locator.Delete();
            policy.Delete();

            return inputAsset;
        }


        static public IAsset EncodeToAdaptiveBitrateMp4Set(IAsset inputAsset)
        {
            //http://msdn.microsoft.com/en-us/library/dn619392.aspx
            //const string encodingPreset = "H264 Adaptive Bitrate MP4 Set 720p, H264 Broadband SD 4x3, H264 Smooth Streaming SD 4x3";
            //const string encodingPreset = "H264 Broadband SD 4x3";
            string encodingPreset = "H264 Adaptive Bitrate MP4 Set 720p";

            IJob job = _context.Jobs.Create(String.Format("Encoding into Mp4 {0} to {1}",
                                    inputAsset.Name,
                                    encodingPreset));

            var mediaProcessors = _context.MediaProcessors.Where(p => p.Name.Contains("Media Encoder")).ToList();

            var latestMediaProcessor = mediaProcessors.OrderBy(mp => new Version(mp.Version)).LastOrDefault();

            ITask encodeTask = job.Tasks.AddNew("Custom Monjin Encoding", latestMediaProcessor, encodingPreset,
                TaskOptions.None);
            encodeTask.InputAssets.Add(inputAsset);
            encodeTask.OutputAssets.AddNew(String.Format("{0} as {1}", inputAsset.Name, "MonjinPreset"), AssetCreationOptions.None);

            job.StateChanged += new EventHandler<JobStateChangedEventArgs>(JobStateChanged);
            job.Submit();
            job.GetExecutionProgressTask(CancellationToken.None).Wait(new TimeSpan(8, 0, 0));

            return job.OutputMediaAssets[0];
        }

        static private string GenerateTokenRequirements()
        {
            var template = new TokenRestrictionTemplate();

            template.PrimaryVerificationKey = new SymmetricVerificationKey();
            template.AlternateVerificationKeys.Add(new SymmetricVerificationKey());
            template.Audience = SampleAudience;
            template.Issuer = SampleIssuer;
            template.RequiredClaims.Add(TokenClaim.ContentKeyIdentifierClaim);

            return TokenRestrictionTemplateSerializer.Serialize(template);
        }

        static private string ConfigurePlayReadyLicenseTemplate()
        {
            // The following code configures PlayReady License Template using .NET classes
            // and returns the XML string.

            var responseTemplate = new PlayReadyLicenseResponseTemplate();
            var licenseTemplate = new PlayReadyLicenseTemplate();

            responseTemplate.LicenseTemplates.Add(licenseTemplate);

            return MediaServicesLicenseTemplateSerializer.Serialize(responseTemplate);
        }

        /// <summary>
        /// Gets the streaming origin locator.
        /// </summary>
        /// <param name="assets"></param>
        /// <returns></returns>
        static public string GetStreamingOriginLocator(IAsset asset)
        {

            // Get a reference to the streaming manifest file from the  
            // collection of files in the asset. 

            var assetFile = asset.AssetFiles.Where(f => f.Name.ToLower().EndsWith(".ism")).FirstOrDefault();

            // Create a 30-day readonly access policy. 
            IAccessPolicy policy = _context.AccessPolicies.Create("Streaming policy",
                TimeSpan.FromDays(30),
                AccessPermissions.Read);

            // Create a locator to the streaming content on an origin. 
            ILocator originLocator = _context.Locators.CreateLocator(LocatorType.OnDemandOrigin, asset,
                policy,
                DateTime.UtcNow.AddMinutes(-5));

            // Create a URL to the manifest file. 
            if (assetFile != null)
                return originLocator.Path + assetFile.Name;
            return null;
        }

        static private void JobStateChanged(object sender, JobStateChangedEventArgs e)
        {
            Console.WriteLine(string.Format("{0}\n  State: {1}\n  Time: {2}\n\n",
                ((IJob)sender).Name,
                e.CurrentState,
                DateTime.UtcNow.ToString(@"yyyy_M_d__hh_mm_ss")));
        }
    }
}