using Microsoft.WindowsAzure.MediaServices.Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Indexing
{
	class Program
	{
		private static CloudMediaContext _context = null;
		private static MediaServicesCredentials _cachedCredentials = null;

		private static readonly string MediaServicesAccountName = ConfigurationManager.AppSettings["MediaServicesAccountName"];
		private static readonly string MediaServicesAccountKey = ConfigurationManager.AppSettings["MediaServicesAccountKey"];

		private const string SingleMp4File = @"C:\Users\user_name\Desktop\abcd.mp4";

		static void Main(string[] args)
		{
			_cachedCredentials = new MediaServicesCredentials(MediaServicesAccountName, MediaServicesAccountKey);

			_context = new CloudMediaContext(_cachedCredentials);

			IAsset asset = CreateAssetAndUploadSingleFile(SingleMp4File);
		}

		static IAsset CreateAssetAndUploadSingleFile(string filePath)
		{
			var assetName = Path.GetFileNameWithoutExtension(filePath);

			IAsset asset = _context.Assets.Create(assetName, AssetCreationOptions.None);

			var assetFile = asset.AssetFiles.Create(Path.GetFileName(filePath));
			assetFile.Upload(filePath);

			return asset;
		}

		static bool RunIndexingJob(IAsset asset, string inputMediaFilePath, string outputFolder, string configurationFile = "")
		{

			// Declare a new job.
			IJob job = _context.Jobs.Create("My Indexing Job");

			// Get a reference to the Windows Azure Media Indexer.
			string MediaProcessorName = "Azure Media Indexer";
			IMediaProcessor processor = GetLatestMediaProcessorByName(MediaProcessorName);

			// Read configuration from file if specified.
			string configuration = string.IsNullOrEmpty(configurationFile) ? "" : File.ReadAllText(configurationFile);

			// Create a task with the encoding details, using a string preset.
			ITask task = job.Tasks.AddNew("My Indexing Task",
				processor,
				configuration,
				TaskOptions.None);

			// Specify the input asset to be indexed.
			task.InputAssets.Add(asset);

			// Add an output asset to contain the results of the job. 
			task.OutputAssets.AddNew("My Indexing Output Asset", AssetCreationOptions.None);

			// Use the following event handler to check job progress.  
			job.StateChanged += new EventHandler<JobStateChangedEventArgs>(StateChanged);

			// Launch the job.
			job.Submit();

			// Check job execution and wait for job to finish. 
			Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);
			progressJobTask.Wait();

			// If job state is Error, the event handling 
			// method for job progress should log errors.  Here we check 
			// for error state and exit if needed.
			if (job.State == JobState.Error)
			{
				Console.WriteLine("Exiting method due to job error.");
				return false;
			}

			// Download the job outputs.
			DownloadAsset(task.OutputAssets.First(), outputFolder);

			return true;
		}
		static void DownloadAsset(IAsset asset, string outputDirectory)
		{
			foreach (IAssetFile file in asset.AssetFiles)
			{
				file.Download(Path.Combine(outputDirectory, file.Name));
			}
		}

		static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
		{
			var processor = _context.MediaProcessors
			.Where(p => p.Name == mediaProcessorName)
			.ToList()
			.OrderBy(p => new Version(p.Version))
			.LastOrDefault();

			if (processor == null)
				throw new ArgumentException(string.Format("Unknown media processor",
														   mediaProcessorName));

			return processor;
		}

		static private void StateChanged(object sender, JobStateChangedEventArgs e)
		{
			Console.WriteLine(string.Format("{0}\n  State: {1}\n  Time: {2}\n\n",
				((IJob)sender).Name,
				e.CurrentState,
				DateTime.UtcNow.ToString(@"yyyy_M_d__hh_mm_ss")));
		}

	}
}
