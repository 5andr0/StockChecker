# StockChecker
MediaMarkt & Saturn Stock Availability Checker based on Selenium with Chrome for GraphQL Cloudflare bypass
### Introduction
This is a hobby project to gain a fighting chance against shopping bots to get hold of a RTX series 3000 card or Ryzen 5000 CPUs.  
Saturn is currently **not** supported, but due to the same frontend, this can be achieved with minor code changes.

## Installation
- Install the latest **.NET Core Runtime 3.1** for your OS from [https://dotnet.microsoft.com/download/dotnet-core/3.1](https://dotnet.microsoft.com/download/dotnet-core/3.1)  
- **Google Chrome** needs to be installed on your system with a minimum version of 87.0.4280.88.  
- For newer Chrome builds you might have to replace *ChromeDriver* in the root dir.  
- Download and extract the latest StockChecker binaries from the [release section](https://github.com/5andr0/StockChecker/releases).  
- Adjust your settings in the config.ini and execute the StockChecker binary.  
- Chrome will initially create and reuse a Chrome Profile located inside the ChromeProfile folder in the root directory.  
- On the first run you will have to consent and accept all cookies and solve a cloudflare captcha if requested.  
- It is recommended to login to your MediaMarkt/Saturn Account to avoid bot detection and ip ban on the GraphQL api.

## Configuration
#### Example config.ini
```bash
[general]
# refresh frequency in minutes - # default is 10 minutes
frequency = 10
# start a headless chrome instance when set to 1
headless = 0

[collections]
# set to 1 to fetch all key-matching RTX cards from www.mediamarkt.de/de/campaign/grafikkarten-nvidia-geforce-rtx-30.html
NVIDIA_RTX_3060_Ti = 0
NVIDIA_RTX_3070 = 0
NVIDIA_RTX_3080 = 0
NVIDIA_RTX_3090 = 0
# example of a custom collection
#RTX3080 = [2681859,2681861,2681869,2681871,2683243,2683227,2683229,2684238,2684241,2683937,2683942,2689452,2689453,2689451,2691438,2691443,2688473]
# product ids to ignore 
#ignore = [2681859, 2681861]
```
#### Notes
- For RTX 30 Cards I recommend to reuse the preset collections from  
[www.mediamarkt.de/de/campaign/grafikkarten-nvidia-geforce-rtx-30.html](www.mediamarkt.de/de/campaign/grafikkarten-nvidia-geforce-rtx-30.html)  
Setting `NVIDIA_RTX_3080 = 1` will include all 3080 cards listed on the rtx 30 campaign page.  
The collection key name is derived dynamically from a GraphqlBreadcrumb field by replacing spaces with underscores:  
`{"__typename":"GraphqlBreadcrumb","name":"NVIDIA RTX 3060 Ti"}`  
That way it will support upcoming collections like the 3080 Ti.

- If you only want to check for OC cards, you can exclude non-oc cards by adding their ID to the ignore collection.  
Product IDs can be found at the end of each product URL.  

- With the `frequency` setting you can adjust the refetch time in minutes to refresh availabilities.  
When a card is available a beep sound melody is played and a new chrome tab pointing to the product will be opened.  
You will only be notified once since available cards will be added to the ignore list.  

- After preparing cookies and logins you can run a headless chrome instance with `headless = 1`  
*This feature has not been tested yet!*  

## Console commands
- **q | quit | exit**  
Gracefully exit Chrome
- **ignore <id1, ..id#>**  
ignore ids and update config
- **reload**  
reload config - *not implemented yet*
- **f | refetch**  
manually refetch product availabilities

## Known Bugs
- The very first api call to fetch availabilities will fail and ask to solve a captcha.  
Just ignore this and wait for the second api request
- The cf captcha injected by an iframe does not work

## License

This Project is licensed under [The Unlicense](https://github.com/5andr0/StockChecker/blob/main/LICENSE).

The binary files of ChromeDriver deployed by [jsakamoto/nupkg-selenium-webdriver-chromedriver](https://github.com/jsakamoto/nupkg-selenium-webdriver-chromedriver) are licensed under the [BSD-3-Clause](https://cs.chromium.org/chromium/src/LICENSE).
