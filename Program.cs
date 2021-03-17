using System;
using System.Net;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using IniParser;
using IniParser.Model;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.DevTools.V89.Network;
using OpenQA.Selenium.DevTools.V89.Fetch;
using DevToolsSessionDomains = OpenQA.Selenium.DevTools.V89.DevToolsSessionDomains;
using RequestPattern = OpenQA.Selenium.DevTools.V89.Fetch.RequestPattern;
using RequestPausedEventArgs = OpenQA.Selenium.DevTools.V89.Fetch.RequestPausedEventArgs;
using GetResponseBodyCommandSettings = OpenQA.Selenium.DevTools.V89.Fetch.GetResponseBodyCommandSettings;
using EnableCommandSettings = OpenQA.Selenium.DevTools.V89.Fetch.EnableCommandSettings;

namespace RTXchecker
{
	public static class Extensions
	{
		public static bool ParseBoolean(this IniData config, in string section, in string key, in bool defaultValue = false)
		{
			var v = config[section][key]?.Trim();
			if (String.IsNullOrEmpty(v)) return defaultValue;
			return v.Equals("1") || v.Equals("true", StringComparison.OrdinalIgnoreCase);
		}
		public static IEnumerable<string> ParseIds(this string s)
			=> Regex.Matches(s, @"\d{2,}").Select(m => m.Value);
	}

	class StockChecker
	{
		Timer timer;
		FileIniDataParser fileIniData;
		IniData config;
		const string configFileName = "config.ini";
		readonly Uri baseAddress = new Uri("https://www.mediamarkt.de");
		readonly HashSet<string> ids = new HashSet<string>();
		readonly HashSet<string> idsIgnored = new HashSet<string>();
		IWebDriver driver;
		DevToolsSession session;
		DevToolsSessionDomains domains;

		void ParseProductInfos(in string body)
		{
			JObject data = JObject.Parse(body);

			var visible = data.SelectToken("data.getProductCollectionItems.visible", errorWhenNoMatch: false);
			if (visible?.Any() == true)
			{
				foreach (var info in from i in visible
									 select new
									 {
										 Id = i.SelectToken("productId")?.ToString(),
										 Title = i.SelectToken("product.title", false)?.ToString(),
										 Url = i.SelectToken("product.url", false)?.ToString(),
										 Price = (decimal?)i.SelectToken("price.price", false),
										 Delivery = (DateTime?)i.SelectToken("availability.delivery.earliest", false)
									 })
				{
					if (info.Delivery.HasValue)
					{
						string maxPrice = config["general"]["max_price"];
						if (!String.IsNullOrEmpty(maxPrice))
						{
							if (info.Price > Convert.ToDecimal(maxPrice))
							{
								Console.WriteLine(info.Title + " is available for " + info.Price + " Euro "
									+ " but exceeds the price limit of " + maxPrice + " Euro");
								continue;
							}
						}

						Console.WriteLine(info.Title + " is available for " + info.Price + " Euro at " + info.Delivery);
						driver.SwitchTo().NewWindow(WindowType.Tab);
						driver.Navigate().GoToUrl("https://www.mediamarkt.de" + info.Url);
						driver.SwitchTo().Window(driver.WindowHandles.First());

						_ = Task.Run(async () =>
						{
							for (int i = 30; i-- > 0;)
							{
								Console.Beep(); // Beep(Int32, Int32) with frequencies is windows only, but Beep() is x-plattform
								await Task.Delay((i & 7) << 5); // alternating melody
							}
							Console.WriteLine("\a"); // Bell character
						}).ConfigureAwait(false);

						ids.Remove(info.Id);
					}
				}
			}
			else
				Console.WriteLine("Response did not contain any GraphqlCampaignProductCollectionProduct:\n" + body);
		}
		void WriteConfig() => fileIniData.WriteFile(configFileName, config);

		void ParseConfig()
		{
			fileIniData = new FileIniDataParser();
			fileIniData.Parser.Configuration.CommentString = "#"; // using TOML specification
			config = fileIniData.ReadFile(configFileName);

			foreach (var key in config.Sections.GetSectionData("collections").Keys.Where(x => x.KeyName != "ignore"))
			{
				var collection = key.Value.ParseIds();
				if (!collection.Any())
					continue;
				ids.UnionWith(collection);
				Console.WriteLine("Added collection " + key.KeyName + " from config with ids " + String.Join(',', collection));
			}

			string ignore = config["collections"]["ignore"];
			if (!String.IsNullOrEmpty(ignore))
			{
				var ignores = ignore.ParseIds();
				if (ignores.Any())
				{
					idsIgnored.UnionWith(ignores);
					Console.WriteLine("Ignoring ids " + String.Join(',', idsIgnored));
				}
			}
		}

		void ParseRTXCollection(in string body)
		{
			for (int start, end = body.Length; (start = body.LastIndexOf("GraphqlCampaignProductCollectionItems", end)) > 0; end = start)
			{
				Match m = new Regex(@"""GraphqlBreadcrumb"",""name"":""([\w ]+30[\w ]+)""", RegexOptions.RightToLeft).Match(body, start);
				if (m.Success)
				{
					var title = Regex.Replace(m.Groups[1].Value, @"\s+", "_");
					Console.WriteLine("Parsing collection " + title); // debug
					if (config.ParseBoolean("collections", title))
					{
						var collectionIds = Regex.Matches(body[start..end], @"""productId"":""(?<id>\d+)").Cast<Match>().Select(m => m.Groups["id"].Value).Distinct();
						ids.UnionWith(collectionIds);
						Console.WriteLine("Added " + title + " ids: " + String.Join(',', collectionIds));
					}
				}
			}

			timer = new System.Threading.Timer(_ => { FetchProductInfos(); }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(Convert.ToDouble(config["general"]["frequency"] ?? "10")));
		}

		void FetchProductInfos()
		{
			if (!ids.Any()) return;
			Console.WriteLine("Fetching product infos");
			var lazyOptions = new
			{
				variables = new
				{
					items = from i in ids select new { id = i, type = "Product", priceOverride = (int?)null }
				}
			};
			IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
			js.ExecuteScript("window.__LOADABLE_LOADED_CHUNKS__.s[0](" + JsonConvert.SerializeObject(lazyOptions) + ")");
		}

		public void Run()
		{
			ParseConfig();
			ChromeOptions options = new ChromeOptions();
			options.AddExcludedArgument("enable-automation");
			options.AddExcludedArgument("enable-logging"); // Suppressing device errors 
			options.AddAdditionalOption("useAutomationExtension", false);
			options.AddArguments("--disable-infobars");
			options.AddArgument("--start-maximized");
			options.AddArguments("--disable-extensions");
			options.AddArguments("--disable-application-cache");
			options.AddArguments("--disable-session-crashed-bubble");
			if (config.ParseBoolean("general", "headless"))
			{
				options.AddArguments("--headless");
				options.AddArguments("--disable-gpu");
			}
			options.AddArguments(@"user-data-dir=" + AppDomain.CurrentDomain.BaseDirectory + "ChromeProfile");
			// TODO: read chrome profile and driver paths from ini and convert to fullpath if relative
			driver = new ChromeDriver(AppDomain.CurrentDomain.BaseDirectory, options, TimeSpan.FromMinutes(3));
			driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(3);
			driver.Manage().Timeouts().PageLoad.Add(TimeSpan.FromHours(1));
			driver.Manage().Timeouts().AsynchronousJavaScript.Add(TimeSpan.FromHours(1));

			IDevTools devTools = driver as IDevTools;
			session = devTools.GetDevToolsSession();
			domains = session.GetVersionSpecificDomains<DevToolsSessionDomains>();

			EventHandler<RequestPausedEventArgs> requestPaused = async (sender, e) =>
			{
				try
				{
					var response = await domains.Fetch.GetResponseBody(new GetResponseBodyCommandSettings() { RequestId = (string)e.RequestId }, default(CancellationToken), 20000);
					var body = (response.Base64Encoded) ? Encoding.UTF8.GetString(Convert.FromBase64String(response.Body)) : response.Body;

					if (e.Request.Url.Contains("vendors~main.js"))
					{
						// set fetchPolicy of GetProductCollectionItems queries to network-only to skip fetching from cache
						body = Regex.Replace(body, @"e.prototype.fetchQueryObservable=function\(\w+,(\w+),\w+\){",
							@"$& if ($1?.query?.definitions?.[0]?.name?.value=='GetProductCollectionItems') $1.fetchPolicy='network-only';");
						//body = Regex.Replace(body, @"queryDeduplication[:=]", @"$& !1 && "); // always set queryDeduplication to false
						//body = Regex.Replace(body, @"fetchPolicy:", @"$& 1 ? 'network-only' : "); // always set fetchPolicy to network-only to skip fetching from cache

						await domains.Fetch.FulfillRequest(new FulfillRequestCommandSettings()
						{
							RequestId = e.RequestId,
							ResponseCode = e.ResponseStatusCode ?? 200,
							Body = Convert.ToBase64String(Encoding.UTF8.GetBytes(body))
						});
						return;
					}
					else if (e.Request.Url.Contains("_app-inspire-campaigns-pages.js"))
					{
						body = Regex.Replace(body, @"=\w+\(Object\(\w+\.useLazyQuery", @"=window.__LOADABLE_LOADED_CHUNKS__.s$&");
						// s[0] will be runLazyQuery with proper scope for the GetProductCollectionItems query
						//body = Regex.Replace(body, @"cacheable[:=]", @"$&!1&&"); // always set cacheable to false

						await domains.Fetch.FulfillRequest(new FulfillRequestCommandSettings()
						{
							RequestId = e.RequestId,
							ResponseCode = e.ResponseStatusCode ?? 200,
							Body = Convert.ToBase64String(Encoding.UTF8.GetBytes(body))
						});
						return;
					}
					else if (e.Request.Url.Contains("https://www.mediamarkt.de/de/campaign/grafikkarten-nvidia-geforce-rtx-30.html"))
					{
						ParseRTXCollection(body);
					}
					else if (e.ResourceType == ResourceType.XHR)
					{
						switch (e.ResponseStatusCode)
						{
							case (long)HttpStatusCode.OK:
								if (e.Request.Url.Contains("operationName=GetProductCollectionItems"))
									ParseProductInfos(body);
								break;
							case (long)HttpStatusCode.TooManyRequests:
							case (long)HttpStatusCode.Forbidden:
								Console.WriteLine("ResponseCode(" + e.ResponseStatusCode + "): Cloudflare captcha needs to be resolved first");
								IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
								// iframe srcdoc is same-origin and allows cloudflare cookies to be set for the root domain inside the iframe
								js.ExecuteScript(
									@"var parent = document.getElementById('root');
									var iframe = document.createElement('iframe');
									iframe.id = 'xhr_response';
									iframe.sandbox = 'allow-same-origin allow-scripts allow-top-navigation allow-forms allow-popups';
									iframe.srcdoc = atob('" + Convert.ToBase64String(Encoding.UTF8.GetBytes(body)) + @"');
									document.getElementById(iframe.id)?.remove();
									parent.prepend(iframe);"
								);
								// TODO: fix error: This web property is not accessible via this address. / use a popup instead
								break;
						}
					}

					await domains.Fetch.ContinueRequest(new ContinueRequestCommandSettings() { RequestId = e.RequestId });
				}
				catch (Exception exception)
				{
					Console.WriteLine("Exception inside RequestPausedEvent: " + exception);
				}
			};

			domains.Fetch.Enable(new EnableCommandSettings()
			{
				Patterns = new RequestPattern[] {
					new RequestPattern() {
						RequestStage = RequestStage.Response,
						ResourceType = ResourceType.Document,
						UrlPattern = "https://www.mediamarkt.de/de/campaign/grafikkarten-nvidia-geforce-rtx-30.html" } ,
					new RequestPattern() {
						RequestStage = RequestStage.Response,
						ResourceType = ResourceType.XHR,
						//UrlPattern = "https://www.mediamarkt.de/api/v1/graphql?operationName=GetProductCollectionItems*" },
						UrlPattern = "https://www.mediamarkt.de/api/v1/*" },
					new RequestPattern() {
						RequestStage = RequestStage.Response,
						ResourceType = ResourceType.Script,
						UrlPattern = "https://assets.mediamarkt.de/webmobile-pwa-public-assets-dev/*" }
				}
			}).Wait();

			domains.Fetch.RequestPaused += requestPaused;
			driver.Navigate().GoToUrl("https://www.mediamarkt.de/de/campaign/grafikkarten-nvidia-geforce-rtx-30.html");
		}

		public void Command(in string cmd, in string[] args = null, in string commandLine = null)
		{
			switch (cmd)
			{
				case "?":
				case "help":
					Console.WriteLine("TODO: command list: q|quit|exit, ignore <id1,..>, f|refetch");
					break;
				case "q":
				case "quit":
				case "exit":
					Console.WriteLine("exit called");
					Dispose();
					Environment.Exit(0);
					break;
				case "ignore":
					var numberSubset = args.Where(x => int.TryParse(x, out _));
					idsIgnored.UnionWith(numberSubset);
					Console.WriteLine("Added ignoring ids: " + String.Join(',', numberSubset));
					config["collections"]["ignore"] = "[" + String.Join(',', idsIgnored) + "]";
					WriteConfig(); // update ignore in config file
					break;
				case "reload":
					// TODO: reload config
					break;
				case "f":
				case "refetch":
					FetchProductInfos();
					break;
				default:
					Console.WriteLine("unknown command. use 'help' or '?' to print commands");
					break;
			}
		}

		public void Dispose()
		{
			timer?.Change(Timeout.Infinite, Timeout.Infinite);
			driver?.Close();
			driver?.Quit();
		}

		~StockChecker()
		{
			Dispose();
		}
	}

	class Program
	{
		static void Main()
		{
			var stockChecker = new StockChecker();
			stockChecker.Run();
			Console.CancelKeyPress += (sender, e) =>
			{
				e.Cancel = true;
				stockChecker.Command("exit");
			};
			while (true)
			{
				string line = Console.ReadLine();
				string[] argv = line.Split(null);
				stockChecker.Command(argv[0], argv.Skip(1).ToArray(), line);
			}
		}
	}
}