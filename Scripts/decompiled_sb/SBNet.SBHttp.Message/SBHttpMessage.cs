using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using SBNet.SmartNetwork;

namespace SBNet.SBHttp.Message;

public class SBHttpMessage
{
	[JsonProperty("I")]
	[DefaultValue("")]
	public string Id;

	[JsonProperty("L")]
	[DefaultValue("")]
	public string License;

	[JsonProperty("T")]
	[DefaultValue("")]
	public string Token;

	public List<RichMessage> Messages;

	public SBHttpMessage()
	{
		Messages = new List<RichMessage>();
		License = string.Empty;
		Token = string.Empty;
		Id = string.Empty;
	}

	public SBHttpMessage(string license, string id, string token)
	{
		License = license;
		Token = token;
		Id = id;
	}

	public SBHttpMessage(string license, string id, string token, List<RichMessage> messages)
	{
		Messages = messages;
		License = license;
		Id = id;
		Token = token;
	}

	public static SBHttpMessage FromJson(byte[] json)
	{
		using MemoryStream stream = new MemoryStream(json);
		using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
		using MemoryStream memoryStream = new MemoryStream();
		gZipStream.CopyTo(memoryStream);
		return JsonConvert.DeserializeObject<SBHttpMessage>(Encoding.UTF8.GetString(memoryStream.ToArray()), new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Ignore,
			DefaultValueHandling = DefaultValueHandling.Ignore
		});
	}

	public static string GetJson(byte[] json)
	{
		using MemoryStream stream = new MemoryStream(json);
		using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
		using MemoryStream memoryStream = new MemoryStream();
		gZipStream.CopyTo(memoryStream);
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	public static byte[] ToJson(SBHttpMessage message)
	{
		string s = JsonConvert.SerializeObject(message, new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Ignore,
			DefaultValueHandling = DefaultValueHandling.Ignore
		});
		using MemoryStream memoryStream = new MemoryStream();
		using MemoryStream memoryStream2 = new MemoryStream(Encoding.UTF8.GetBytes(s));
		using (GZipStream destination = new GZipStream(memoryStream, CompressionMode.Compress))
		{
			memoryStream2.CopyTo(destination);
		}
		return memoryStream.ToArray();
	}
}
