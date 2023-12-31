using System.Diagnostics.CodeAnalysis;
using System.Media;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace binChecker;

public static class Program
{
    private const int GreenHue = 21905;
    private const int YellowHue = 10443;
    private const string Reg = "<span class=\"infocontentcol\"><u>(Next Waste Collections)<\\/u><\\/span><br><span class=\"infocontentcol\">(Waste): ?<\\/span>([a-zA-Z,0-9 ]+)<br><span class=\"infocontentcol\">(Green): ?<\\/span>([a-zA-Z,0-9 ]+)<br><span class=\"infocontentcol\">(Recycle): ?<\\/span>([a-zA-Z,0-9 ]+)<\\/div>";

    private static Dictionary<Argument, object> GetArguments(string[] args)
    {
        Dictionary<Argument, object> returnObject = new();

        var tempKey = string.Empty;
        List<object>? tempObj = null;

        foreach (var arg in args)
        {
            Console.WriteLine(arg);
            if (arg.StartsWith('-') && tempObj is not null)
            {
                returnObject.Add(GetKey(tempKey), tempObj.Count == 1 ? tempObj.First() : tempObj);
                tempKey = string.Empty;
                tempObj = null;
            }

            if (arg.StartsWith('-') && !string.IsNullOrEmpty(tempKey))
            {
                returnObject.Add(GetKey(tempKey), true);
                tempKey = arg;
                continue;
            }

            if (arg.StartsWith('-'))
            {
                tempKey = arg;
                continue;
            }

            if (tempObj is null)
            {
                tempObj = new List<object> { arg };
                continue;
            }

            tempObj.Add(arg);
        }

        if (tempObj is not null)
        {
            returnObject.Add(GetKey(tempKey), tempObj.Count == 1 ? tempObj.First() : tempObj);
        }
        else if (!string.IsNullOrEmpty(tempKey))
        {
            returnObject.Add(GetKey(tempKey), true);
        }

        return returnObject;
    }

    private static Argument GetKey(string key)
    {
        if (key.Equals("-n", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("--no-lights", StringComparison.OrdinalIgnoreCase)) return Argument.NoLights;

        if (key.Equals("-p", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("--property-number", StringComparison.OrdinalIgnoreCase)) return Argument.PropertyNumber;

        return Argument.None;
    }

    private static async Task Main(string[] args)
    {
        var arguments = GetArguments(args);
        var lights = true;
        try
        {
            if (arguments.TryGetValue(Argument.NoLights, out var noLights))
            {
                lights = !(bool)noLights;
            }
        }
        catch
        {
            // ignored
        }

        string propertyNumber;
        try
        {
            if (arguments.TryGetValue(Argument.PropertyNumber, out var p))
            {
                propertyNumber = (string)p;
            }
            else
            {
                throw new Exception();
            }
        }
        catch
        {
            Console.WriteLine("Unable to continue with no property number!");
            Console.WriteLine("Try setting the cli argument -p");
            Console.ReadKey();
            return;
        }


        var schedule = await GetScheduleAsync(propertyNumber);
        if (schedule is null)
            return;

        if (!lights)
        {
            Console.ForegroundColor = schedule.IsRecycle ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.WriteLine(JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true }));

            Console.ReadLine();
            return;
        }

        LightResponse? originalState;
        var lightClient = await GetLightClient();

        if (lightClient is null)
        {
            Console.WriteLine("Unable to collect client");
            return;
        }

        try
        {
            var lightResponse = await lightClient.GetAsync("lights/2");
            var lightResponseContent = await lightResponse.Content.ReadAsStringAsync();

            originalState = JsonSerializer.Deserialize<LightResponse>(lightResponseContent);
            if (originalState is null)
            {
                Console.WriteLine("Could not find light");
                return;
            }
        }
        catch
        {
            return;
        }

        await Task.WhenAll(
            ToggleLights(lightClient, originalState, schedule.IsRecycle ? YellowHue : GreenHue),
            PlaySound(schedule)
        );
    }

    private static async Task ToggleLights(HttpClient lightClient, LightResponse originalState, int colour)
    {
        await Cycle(async () =>
            {
                await lightClient.PutAsync(
                    "lights/2/state",
                    new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            on = true,
                            hue = colour,
                            bri = originalState.State.Brightness
                        }),
                        Encoding.UTF8,
                        "application/json"));
            },
            async () =>
            {
                await lightClient.PutAsync(
                    "lights/2/state",
                    new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            on = false,
                        }),
                        Encoding.UTF8,
                        "application/json"));
            });

        await lightClient.PutAsync(
            "lights/2/state",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    on = true,
                    hue = originalState.State.Hue,
                    bri = originalState.State.Brightness
                }),
                Encoding.UTF8,
                "application/json")
        );

        await lightClient.PutAsync(
            "lights/2/state",
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    on = originalState.State.On,
                    hue = originalState.State.Hue,
                    bri = originalState.State.Brightness
                }),
                Encoding.UTF8,
                "application/json")
        );
    }

    private static async Task<HttpClient?> GetLightClient()
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        handler.ServerCertificateCustomValidationCallback += (_, _, _, _) => true;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

        var url = new Uri("https://192.168.4.29/api/qdjy5vwSFGlTujw3QBn-ADiL0yAlq0jl4965doKN/");
        Console.WriteLine($"Trying {url.AbsoluteUri}");

        var client = new HttpClient(handler)
        {
            BaseAddress = url
        };

        client.DefaultRequestHeaders.Add("hue-application-key", "84345C45D5D85F67848AFBE74699C7F8");
        try
        {
            await client.GetAsync("");
        }
        catch
        {
            Console.WriteLine($"Failed to target {url.AbsoluteUri}");
        }

        return client;
    }

    private static async Task Cycle(Func<Task> on, Func<Task> off)
    {
        for (var i = 0; i < 5; i++)
        {
            await on();
            await Task.Delay(20 * 100);
            await off();
        }
    }

    private static async Task<Schedule?> GetScheduleAsync(string propertyNumber)
    {
        var regex = new Regex(Reg);
        var binClient = new HttpClient
        {
            BaseAddress = new Uri($"https://digital.wyndham.vic.gov.au/myWyndham/init-map-data.asp?propnum={propertyNumber}")
        };
        try
        {
            var response = await binClient.GetStringAsync(string.Empty);
            Console.WriteLine("Got bin state");
            var matches = regex.Match(response);

            var schedule = new Schedule
            {
                Waste = DateTime.Parse(matches.Groups[3].Value),
                Green = DateTime.Parse(matches.Groups[5].Value),
                Recycle = DateTime.Parse(matches.Groups[7].Value),
                Set = true
            };
            return schedule;
        }
        catch
        {
            Console.WriteLine("Failed to cast");
            return null;
        }
    }

    private const string RecycleSound = "./Recycle.wav";
    private const string GreenWasteSound = "./GreenWaste.wav";

    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
    private static async Task PlaySound(Schedule schedule)
    {
        await Task.Delay(1000);
        var player = new SoundPlayer(Path.Join(Directory.GetCurrentDirectory(),
            schedule.IsRecycle ? RecycleSound : GreenWasteSound));
        player.Play();
    }
}