using NSec.Cryptography;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using publicKey = NSec.Cryptography.PublicKey;
using System.Text;
using Newtonsoft.Json.Linq;
using Npgsql;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.AspNetCore;

//Warns are for losers
#pragma warning disable CS8618, CS1998, CS8600, CS8604, CS0168, CS8602, CS4014
class Program {
    public static Random rand = new Random();
    private static HttpClient client = new HttpClient();

    private static NpgsqlConnection dbConnection;

    private static Dictionary<string, string> emoji = new Dictionary<string, string>(){
        {"bell",char.ConvertFromUtf32(0x1F514)},
        {"santa",char.ConvertFromUtf32(0x1F385)},
        {"christmas_tree",char.ConvertFromUtf32(0x1F384)},
        {"earth_europe",char.ConvertFromUtf32(0x1F30D)},
        {"earth_asia",char.ConvertFromUtf32(0x1F30F)},
        {"tada",char.ConvertFromUtf32(0x1F388)},
        {"balloon",char.ConvertFromUtf32(0x1F389)},
        {"cat",char.ConvertFromUtf32(0x1F431)},
        {"fox",char.ConvertFromUtf32(0x1F98A)},
        {"popping_cork",char.ConvertFromUtf32(0x1F37E)},
        {"pickaxe",char.ConvertFromUtf32(0x26CF)},
        {"heart_face",char.ConvertFromUtf32(0x1F970)},
        {"heart_arrow",char.ConvertFromUtf32(0x1F498)},
        {"gun",char.ConvertFromUtf32(0x1F52B)},
        {"eyebrow_raise",char.ConvertFromUtf32(0x1F928)},
        {"globe",char.ConvertFromUtf32(0x1F310)},
        {"calendar",char.ConvertFromUtf32(0x1F5D3)},
        {"cake",char.ConvertFromUtf32(0x1F370)},
        {"ghost",char.ConvertFromUtf32(0x1F47B)},
        {"alien",char.ConvertFromUtf32(0x1F47D)},
    };
    private static Dictionary<(int Day, int Month), string> CustomBongs = new Dictionary<(int, int), string>{
        //Christmas is just a week away!
        {(25,12),$"{emoji["santa"]} Christmas Bong {emoji["christmas_tree"]}"},
        //New year :stare:
        {(1, 1),$"{emoji["popping_cork"]} New Year Bong {emoji["tada"]}"},
        //Kae's birthday
        {(2, 2),$"{emoji["fox"]} Kae's Birthday Bong {emoji["balloon"]}"},
        //MOLES MOLES MOLES
        {(12, 2),$"{emoji["pickaxe"]} MOLES MOLES MOLES Bong {emoji["pickaxe"]}"},
        //Valentine's day bbg mwag
        {(14, 2),$"{emoji["heart_arrow"]} Valentine's Day Bong {emoji["heart_face"]}"},
        //Sus day
        {(1, 4),$"{emoji["gun"]} Bang {emoji["eyebrow_raise"]}"},
        //Earth day!
        {(22, 4),$"{emoji["earth_europe"]} Earth Bong {emoji["earth_asia"]}"},
        //oh god
        {(2, 7),$"{emoji["calendar"]} Middle of The Year Bong {emoji["globe"]}"},
        //Mi birthday innit *says in bri'ish accent*
        {(8, 7),$"{emoji["cat"]} Catdotjs's Birthday Bong {emoji["balloon"]}"},
        {(6, 9),$"{emoji["cake"]} Birthday Bong {emoji["balloon"]}"},
        {(31, 10),$"{emoji["ghost"]} SpOoOkY Bong {emoji["alien"]}"},
    };

    protected static JObject Config;

    private static Dictionary<string,List<(string,string)>> ListOfBongedPeople = new Dictionary<string, List<(string, string)>>();
    private static Dictionary<string, SemaphoreSlim> ListOfSemaphores = new Dictionary<string, SemaphoreSlim>();
    private static List<(string,int)> ButtonStyleInfo = new List<(string, int)>() {
        {("🥇",3)},
        {("🥈",1)},
        {("🥉",4)},
    };

    //Ed25519
    private static SignatureAlgorithm sig = SignatureAlgorithm.Ed25519;
    private static publicKey pub;
    
    //Background Worker
    private static BackgroundWorker webServer = new BackgroundWorker();

    //Cached API stuff
    private static JObject funfact;
    private static JObject catImage;

    public static void Main(string[] args) {
        //Get keys

        Config=JObject.Parse(File.ReadAllText("Key\\APIKeys.json"));
        client.DefaultRequestHeaders.Authorization =new AuthenticationHeaderValue("Bot", (string)Config["DiscordAPI"]);
        pub = publicKey.Import(sig, Convert.FromHexString((string)Config["DiscordPubKey"]), KeyBlobFormat.RawPublicKey);

        //Sets up server
        var builder = WebApplication.CreateBuilder(args);

        //Use PFX you dumbo
        
        IPAddress ip = IPAddress.Parse((string)Config["PrivateIP"]);
        /*
        Console.WriteLine(ip.ToString());
        X509Certificate2 cert = new X509Certificate2("Key\\certificate.pfx", (string)Config["SSLPassword"]);
        */
        builder.WebHost.UseKestrel(options => {
            options.Listen(ip, 443, listenOpt => {
                //listenOpt.UseHttps(cert);
            });
            options.Listen(ip, 80);
        });

        var app = builder.Build();
        app.UseHttpsRedirection();

        //Interactions manager
        app.MapPost("/Interactions/", async (HttpRequest req, HttpResponse res) => {
            (bool IsValid, string Body) result = await IsValidRequest(req);
            if(result.IsValid) {
                JObject reqJSON = JObject.Parse(result.Body);


                res.StatusCode=200;
                res.ContentType="application/json";
                switch((int)reqJSON["type"]) { 
                    case 2:
                        if((string)reqJSON["data"]["name"]=="select_bong_channel") {
                            await res.WriteAsync((await OnSelectBongChannel(reqJSON)).ToString());
                        }
                        break;

                    case 3:
                        string GuildId = (string)reqJSON["channel"]["guild_id"];

                        //Let's see if there is a semaphore
                        try {
                            if(!ListOfSemaphores.ContainsKey(GuildId)) {
                                ListOfSemaphores[GuildId]=new SemaphoreSlim(1, 1);
                            }
                        } finally { };

                        //Only bongs can activate this
                        await ListOfSemaphores[GuildId].WaitAsync();
                        try {
                            await res.WriteAsync((await OnBongPress(reqJSON)).ToString());
                        } finally {
                            ListOfSemaphores[GuildId].Release();
                        }
                    break;
                }

            } else {
                res.StatusCode=401;
            }
        });
        
        //Bong Manager
        webServer.DoWork+=async (a, b) => {
            //Database time bbg
            dbConnection=new NpgsqlConnection((string)Config["Postgres"]);
            await dbConnection.OpenAsync();
            using(StreamReader SQLDB = File.OpenText("Key\\dbinit.sql")) {
                try {
                    await using(NpgsqlCommand cmd = new NpgsqlCommand(await SQLDB.ReadToEndAsync(),dbConnection)) {
                        await cmd.ExecuteNonQueryAsync();
                        Console.WriteLine($"[{DateTime.Now}] Made database :O");
                    }
                } catch(Exception ex) { } //don care :snooze:
            }

            //Slash commands add :>
            JObject slashCommands = new JObject() {
                {"name", "select_bong_channel"},
                {"type", 1},
                {"description", "Choose which channel bot will send bong messages to"},
                {"options", new JArray(){ 
                    new JObject() {
                        {"name","channel"},
                        {"description","channel to send bong messages"},
                        {"type", 7},
                        {"required",true}
                    }
                } },
            };
            await client.PostAsync($"https://discord.com/api/applications/{Config["DiscordAppId"]}/commands", new StringContent(slashCommands.ToString(), Encoding.UTF8, "application/json"));

            int hour = -1;
            while(true) {

                //Check if bong has been send this hour
                if(DateTime.Now.Minute==0&&DateTime.Now.Hour!=hour) {
                    hour=DateTime.Now.Hour;
                    SendGlobalBongs();
                }
            }
        };

        webServer.RunWorkerAsync();
        app.Run();
    }
    public static async void SendGlobalBongs() {
        using(NpgsqlCommand command = new NpgsqlCommand("SELECT * FROM webhooks", dbConnection)) {
        using(NpgsqlDataReader reader = await command.ExecuteReaderAsync()) {
            try {
                //Gets fun fact and cat picture
                funfact = await GetFunFact();
                catImage = await GetCatImage();
                while(await reader.ReadAsync()) {
                        DispatchBong(reader.GetString(1), reader.GetString(0));
                }
            } catch(Exception ex) { };
        }
        }
    }
   
    //Create and Dispatch Bongs
    public async static Task DispatchBong(string Webhook,string guild_id) {
        //Clear bong list
        ListOfBongedPeople.Clear();

        //Builds message to send
        JObject obj = new JObject();

        //A Bodge to get current bong, not clever, just a quick fix
        string CurrentBong = $"{emoji["bell"]} Bong {emoji["bell"]}";
        try { CurrentBong=CustomBongs[(DateTime.Now.Day, DateTime.Now.Month)]; } catch(Exception ex) { };

        obj.Add("embeds",
            new JArray(){
                await CreateBong(CurrentBong),
            });
        obj.Add("components",
            new JArray(){
                new JObject() {
                    { "type", 1},
                    { "components", new JArray(){
                        { CreateButton("Bong!", "bong", emoji_id:"699705094253576222",style:2) }
                    }}
                }
            });
        await client.PostAsync(Webhook, new StringContent(obj.ToString(), Encoding.UTF8, "application/json"));
    }
    public async static Task<JObject> CreateBong(string bongTitle) {
        JObject embed = new JObject() {
        { "title", bongTitle},
        { "description", "__**Fun fact:**__ "+ (string)funfact["text"]},
        { "color", rand.Next(0x808080, 0xFFFFFF)}, //Fuck you dj
        { "image", catImage},
        { "footer", SetFooter("this bot is made by catdotjs#6969. Please inform them with any problem.", @"https://cdn-icons-png.flaticon.com/512/179/179386.png")}
        };
        return embed;
    }
    public async static Task<JObject> GetCatImage() {
        JObject Image = new JObject();

        HttpRequestMessage CatAPIRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.thecatapi.com/v1/images/search");
        CatAPIRequest.Headers.Add("x-api-key", (string)Config["CatAPIKey"]);

        HttpResponseMessage resp = await client.SendAsync(CatAPIRequest);
        JArray responseJson = JArray.Parse(await resp.Content.ReadAsStringAsync());

        Image.Add("url", responseJson[0]["url"]);
        return Image;
    }
    public async static Task<JObject> GetFunFact() {
        JObject fact = new JObject();

        HttpRequestMessage FactAPIRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.api-ninjas.com/v1/facts?limit=1");
        FactAPIRequest.Headers.Add("X-Api-Key", (string)Config["APINinja"]);

        HttpResponseMessage resp = await client.SendAsync(FactAPIRequest);
        JArray responseJson = JArray.Parse(await resp.Content.ReadAsStringAsync());

        fact.Add("text", responseJson[0]["fact"]);
        return fact;
    }
    public static JObject SetFooter(string name, string icon_url = "") {
        JObject footer = new JObject(){{ "text", name }};
        if(icon_url!="") {
            footer.Add("icon_url", icon_url);
        }
        return footer;
    }
    public static JObject CreateButton(string text, string custom_id, string? emoji_id = null, string? emoji_name=null,bool disabled=false, int style = 1) {
        JObject button = new JObject() {
            { "type",2 },
            { "label",text },
            { "style",style },
            { "custom_id",custom_id },
            { "disabled", disabled}
        };
        if(emoji_id!=null||emoji_name!=null) {
            button.Add("emoji", CreateEmoji(emoji_id, emoji_name));
        }
        return button;
    }
    public static JObject CreateEmoji(string emoji_id,string? name=null) {
        return new JObject(){{ "id", emoji_id }, { "name", name } };
    }

    //Interactions
    public async static Task<JObject> OnSelectBongChannel(JObject reqJSON) {
        JObject resJSON = new JObject();

        //Response message
        string channelID = (string)reqJSON["data"]["options"][0]["value"];
        resJSON.Add("type", 4);
        resJSON.Add("data", new JObject() {
            { "content",$"<#{channelID}> has been set as your bong channel"}, 
            { "flags",64}
        });

        //Create webhook
        HttpResponseMessage webhookResponse = await client.PostAsync(
            $"https://discord.com/api/channels/{channelID}/webhooks",
            new StringContent("{\"name\":\"BigBen\"}", Encoding.UTF8, "application/json"));

        //Read and create a webhook link then save it to db
        using(StreamReader ContentReader = new StreamReader(webhookResponse.Content.ReadAsStream(), Encoding.UTF8)) {
            JObject ContentJSON = JObject.Parse(await ContentReader.ReadToEndAsync());
            string WebhookGenerated = $"https://discord.com/api/webhooks/{ContentJSON["id"]}/{ContentJSON["token"]}";

            using(NpgsqlCommand command = new NpgsqlCommand("", dbConnection)) {
                command.CommandText="INSERT INTO webhooks (guildid,webhook) VALUES (@guildid,@webhook) ON CONFLICT (guildid) DO UPDATE SET webhook=@webhook";
                command.Parameters.AddWithValue("guildid", (string)ContentJSON["guild_id"]);
                command.Parameters.AddWithValue("webhook", WebhookGenerated);
                await command.ExecuteNonQueryAsync();
            };

        }
        return resJSON;
    }
    public async static Task<JObject> OnBongPress(JObject reqJSON) {
        //Response Message
        JObject resJSON = new JObject() {
            {"type", 4},
            {"data", new JObject(){ 
                { "content", "You really shouldn't see this >~<"},
                { "flags", 64},
            }},
        };
        TimeSpan BongMessageTime = (DateTime.Now - (DateTime)reqJSON["message"]["timestamp"]);
        
        //Did user actually bong in correct time and was it once?
        if(BongMessageTime.Hours==0) {
            //Did user went bongers before?
            string UserId = (string)reqJSON["member"]["user"]["id"];
            string username = (string)reqJSON["member"]["user"]["username"];
            string GuildId = (string)reqJSON["channel"]["guild_id"];

            //This may cause reward being given out twice some very very very rare times
            try {
                if(!ListOfBongedPeople.ContainsKey(GuildId)) {
                    ListOfBongedPeople.Add(GuildId, new List<(string, string)>());
                }
            }finally{ };

            if(!ListOfBongedPeople[GuildId].Contains((UserId, username))) {
                resJSON["data"]["content"]=$"Bong registered! It has been {Math.Round(BongMessageTime.TotalSeconds,2)} seconds since the bong happened.";
                ListOfBongedPeople[GuildId].Add((UserId, username));
                await OnValidBongPress(GuildId, UserId, username, reqJSON);
            } else {
                resJSON["data"]["content"]="You have already bong'd this message before.";
            }
        } else {
            resJSON["data"]["content"]="You can't bong a message from the past.";
        }

        return resJSON;
    }
    public static async Task OnValidBongPress(string GuildId,string UserId, string Username, JObject reqJSON) {
        // 3 things need to be done in this function
        // 1st see if user was first. if not don't bother with points
        // 2nd if they are first. Get their current bong count.
        // 3rd increment the bong by one and set it back. doing this also can help with username updates.
        int count = ListOfBongedPeople[GuildId].Count;

        if(count==1) {
            int BongCount = 0;
            //Read from db
            using(NpgsqlCommand command = new NpgsqlCommand($"SELECT bongs FROM leaderboards WHERE userid='{UserId}'", dbConnection)) {
            using(NpgsqlDataReader reader = await command.ExecuteReaderAsync()) {
                while(await reader.ReadAsync()) {
                    try {
                        //They are in the database 100%
                        BongCount=reader.GetInt32(0);
                    } finally{ }; //They are most probably not in the database, not a problem since next step
                }
            }
            }
            //Increment bb wooooooo
            BongCount++;

            //Write it to db
            using(NpgsqlCommand command = new NpgsqlCommand("", dbConnection)) {
                command.CommandText="INSERT INTO leaderboards (userid,bongs,username) VALUES (@userid,@bongs,@username) ON CONFLICT (userid) DO UPDATE SET bongs=@bongs,username=@username";
                command.Parameters.AddWithValue("userid", UserId);
                command.Parameters.AddWithValue("bongs", BongCount);
                command.Parameters.AddWithValue("username", Username);
                await command.ExecuteNonQueryAsync();
            };
        }
        
        //Get webhook
        string webhookURL = "";
        using(NpgsqlCommand command = new NpgsqlCommand($"SELECT webhook FROM webhooks WHERE guildid='{GuildId}'", dbConnection)) {
        using(NpgsqlDataReader reader = await command.ExecuteReaderAsync()) {
            while(await reader.ReadAsync()) {
                webhookURL=reader.GetString(0);
            }
        }
        }
        
        JObject obj = new JObject();
        JObject Comp = new JObject() {
            { "type",1},
            { "components", new JArray()}
        };
        //Update Count
        JArray ActionRowComp = new JArray() {{ CreateButton($"{count} Bong(s)!","bong", emoji_id: "699705094253576222", style: 2) }};

        //Put them in local bong leaderboard
        for(int i=0;i<Math.Clamp(count,1,3);i++) {
            (string emoji, int style) Styling = ButtonStyleInfo[i];
            (string userid, string username) userData = ListOfBongedPeople[GuildId][i];
            ActionRowComp.Add(CreateButton(userData.username, $"numb_{i}",emoji_name:Styling.emoji,disabled:true,style:Styling.style));
        }

        Comp["components"] = ActionRowComp;
        obj.Add("components", new JArray() { Comp });

        //Edit text
        await client.PatchAsync($"{webhookURL}/messages/{reqJSON["message"]["id"]}", new StringContent(obj.ToString(), Encoding.UTF8, "application/json"));
    }

    //ETC...
    public static async Task<(bool, string)> IsValidRequest(HttpRequest request) {
        byte[] ed = Convert.FromHexString(request.Headers["X-Signature-Ed25519"]);
        using(StreamReader str = new StreamReader(request.Body, Encoding.UTF8)) {
            string body = await str.ReadToEndAsync();
            string data = request.Headers["X-Signature-Timestamp"];
            data+=body;
            return (sig.Verify(pub, Encoding.UTF8.GetBytes(data), ed), body);
        }
    }
}