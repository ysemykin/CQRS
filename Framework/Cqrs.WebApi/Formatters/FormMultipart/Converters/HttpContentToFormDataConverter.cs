﻿#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Cqrs.WebApi.Formatters.FormMultipart.Infrastructure;

namespace Cqrs.WebApi.Formatters.FormMultipart.Converters
{
	/// <summary>
	/// Converts content in a <see cref="HttpContent"/> to <see cref="FormData"/>.
	/// </summary>
	public class HttpContentToFormDataConverter
	{
		/// <summary>
		/// Converts the provided <paramref name="content"/> to multi-part form-data.
		/// </summary>
		public async Task<FormData> Convert(HttpContent content)
		{
			if(content == null)
				throw new ArgumentNullException("content");

			//commented to provide more details about incorrectly formatted data from ReadAsMultipartAsync method
			/*if (!content.IsMimeMultipartContent())
			{
				throw new Exception("Unsupported Media Type");
			}*/

			//http://stackoverflow.com/questions/15201255/request-content-readasmultipartasync-never-returns
			MultipartMemoryStreamProvider multipartProvider = null;

			await Task.Factory
				.StartNewSafely(() =>
					{
						try
						{
							multipartProvider = content.ReadAsMultipartAsync().Result;
						}
						catch (AggregateException aggregateException)
						{
							if (aggregateException.InnerExceptions.Count != 1)
								throw;
							var exception = aggregateException.InnerExceptions.Single() as IOException;
							if (exception == null || exception.Message != @"Unexpected end of MIME multipart stream. MIME multipart message is not complete.")
								throw;

							Stream reqStream = content.ReadAsStreamAsync().Result;
							MemoryStream tempStream = new MemoryStream();
							reqStream.CopyTo(tempStream);

							tempStream.Seek(0, SeekOrigin.End);
							StreamWriter writer = new StreamWriter(tempStream);
							writer.WriteLine();
							writer.Flush();
							tempStream.Position = 0;


							StreamContent streamContent = new StreamContent(tempStream);
							foreach (var header in content.Headers)
								streamContent.Headers.Add(header.Key, header.Value);

							// Read the form data and return an async task.
							multipartProvider = streamContent.ReadAsMultipartAsync().Result;
						}
					},
					CancellationToken.None,
					TaskCreationOptions.LongRunning, // guarantees separate thread
					TaskScheduler.Default);

			var multipartFormData = await Convert(multipartProvider);
			return multipartFormData;
		}

		/// <summary>
		/// Converts the <see cref="MultipartStreamProvider.Contents"/> of the provided <paramref name="multipartProvider"/> to multi-part form-data.
		/// </summary>
		public async Task<FormData> Convert(MultipartMemoryStreamProvider multipartProvider)
		{
			var multipartFormData = new FormData();

			foreach (var file in multipartProvider.Contents.Where(x => IsFile(x.Headers.ContentDisposition)))
			{
				var name = UnquoteToken(file.Headers.ContentDisposition.Name);
				string fileName = FixFilename(file.Headers.ContentDisposition.FileName);
				string mediaType = file.Headers.ContentType.MediaType;

				using (var stream = await file.ReadAsStreamAsync())
				{
					byte[] buffer = ReadAllBytes(stream);
					if (buffer.Length > 0)
					{
						multipartFormData.Add(name, new HttpFile(fileName, mediaType, buffer));
					}
				}
			}

			foreach (var part in multipartProvider.Contents.Where(x => x.Headers.ContentDisposition.DispositionType == "form-data"
																  && !IsFile(x.Headers.ContentDisposition)))
			{
				var name = UnquoteToken(part.Headers.ContentDisposition.Name);
				var data = await part.ReadAsStringAsync();
				multipartFormData.Add(name, data);
			}

			return multipartFormData;
		} 

		private bool IsFile(ContentDispositionHeaderValue disposition)
		{
			return !string.IsNullOrEmpty(disposition.FileName);
		}

		/// <summary>
		/// Remove bounding quotes on a token if present
		/// </summary>
		private static string UnquoteToken(string token)
		{
			if (String.IsNullOrWhiteSpace(token))
			{
				return token;
			}

			if (token.StartsWith("\"", StringComparison.Ordinal) && token.EndsWith("\"", StringComparison.Ordinal) && token.Length > 1)
			{
				return token.Substring(1, token.Length - 2);
			}

			return token;
		}

		/// <summary>
		/// Amend filenames to remove surrounding quotes and remove path from IE
		/// </summary>
		private static string FixFilename(string originalFileName)
		{
			if (string.IsNullOrWhiteSpace(originalFileName))
				return string.Empty;

			var result = originalFileName.Trim();
			
			// remove leading and trailing quotes
			result = result.Trim('"');

			// remove full path versions
			if (result.Contains("\\"))
				result = Path.GetFileName(result);

			return result;
		}

		private byte[] ReadAllBytes(Stream input)
		{
			using (var stream = new MemoryStream())
			{
				input.CopyTo(stream);
				return stream.ToArray();
			}
		}
	}
}