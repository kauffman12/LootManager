using log4net;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Windows;
using System.Threading;

namespace LootManager
{
  static class TokenManager
  {
    private static readonly ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    // This is the OAuth 2.0 Client Secret retrieved
    // above.  Be sure to store this value securely.  Leaking this
    // value would enable others to act on behalf of your application!
    public static string CLIENT_SECRET;

    private static string CLIENT_ID = "454647371816-r8gv4nnmfqbe8ujk3t0mc9g04uk5voh6.apps.googleusercontent.com";
    private static string AUTH_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth";
    private static string TOKEN_REQUEST_URI = "https://www.googleapis.com/oauth2/v4/token";
    private static string SCOPE = "openid profile https://www.googleapis.com/auth/spreadsheets https://www.googleapis.com/auth/drive";

    private static Dictionary<string, string> tokens = null;
    private static System.DateTime expireTime;

    public static void authenticate()
    {
      try
      {
        // stored in properties file installed with application
        CLIENT_SECRET = RuntimeProperties.getProperty("client_secret");

        // init expire time
        expireTime = System.DateTime.Now;

        // wait for token to be received
        //tokenEndpointDecoded["access_token"];
        //tokenEndpointDecoded["refresh_token"];
        //tokenEndpointDecoded["expires_in"]; 
        tokens = getAccessCodeAsync().GetAwaiter().GetResult();

        // update expire time based on token
        expireTime = expireTime.AddSeconds(System.Double.Parse(tokens["expires_in"]));
      }
      catch (System.Exception e)
      {
        MessageBox.Show("Could not Authenticate - check log file for details");
        LOG.Error("Could not Authenticate", e);
      }
    }

    public static void cleanup()
    {
      // disgard tokens
      tokens = null;
    }

    public static string getAccessToken()
    {
      string token = null;

      if (tokens != null)
      {
        if (System.DateTime.Now > expireTime)
        {
          Thread myThread = new Thread(() =>
          {
            expireTime = System.DateTime.Now;
            Dictionary<string, string> result = RefreshTokens().GetAwaiter().GetResult();
            tokens["access_token"] = result["access_token"];
            tokens["expires_in"] = result["expires_in"];
            expireTime = expireTime.AddSeconds(double.Parse(tokens["expires_in"]));
          });

          myThread.Start();
          myThread.Join();
        }
      }

      token = tokens["access_token"];
      return token;
    }

    private static async Task<Dictionary<string, string>> getAccessCodeAsync()
    {
      // Generates state and PKCE values.
      string state = randomDataBase64url(32);
      string code_verifier = randomDataBase64url(32);
      string code_challenge = base64urlencodeNoPadding(sha256(code_verifier));
      const string code_challenge_method = "S256";

      // Creates a redirect URI using an available port on the loopback address.
      string redirectURI = string.Format("http://{0}:{1}/", IPAddress.Loopback, GetRandomUnusedPort());

      // Creates the OAuth 2.0 authorization request.
      string authorizationRequest = string.Format("{0}?response_type=code&scope={1}&redirect_uri={2}&client_id={3}&state={4}&code_challenge={5}&code_challenge_method={6}",
          AUTH_ENDPOINT,
          System.Uri.EscapeDataString(SCOPE),
          System.Uri.EscapeDataString(redirectURI),
          CLIENT_ID,
          state,
          code_challenge,
          code_challenge_method);

      // Opens request in the browser.
      System.Diagnostics.Process.Start(authorizationRequest);

      // Creates an HttpListener to listen for requests on that redirect URI.
      var http = new HttpListener();
      http.Prefixes.Add(redirectURI);
      http.Start();

      // Waits for the OAuth authorization response.
      var context = await http.GetContextAsync();

      // Sends an HTTP response to the browser.
      var response = context.Response;
      string responseString = string.Format("<html><head></head><body>Authentication Successful. You may close the browser.</body></html>");
      var buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
      response.ContentLength64 = buffer.Length;
      var responseOutput = response.OutputStream;
      Task responseTask = responseOutput.WriteAsync(buffer, 0, buffer.Length).ContinueWith((task) =>
      {
        responseOutput.Close();
        http.Stop();
        LOG.Info("HTTP server stopped.");
      });

      // Checks for errors.
      if (context.Request.QueryString.Get("error") != null)
      {
        throw new System.Exception(context.Request.QueryString.Get("error"));
      }
      if (context.Request.QueryString.Get("code") == null
          || context.Request.QueryString.Get("state") == null)
      {
        throw new System.Exception("Malformed authorization response. " + context.Request.QueryString);
      }

      // extracts the code
      var code = context.Request.QueryString.Get("code");
      var incoming_state = context.Request.QueryString.Get("state");

      // Compares the receieved state to the expected value, to ensure that
      // this app made the request which resulted in authorization.
      if (incoming_state != state)
      {
        throw new System.Exception(System.String.Format("Received request with invalid state ({0})", incoming_state));
      }

      return PerformCodeExchange(code, code_verifier, redirectURI).Result;
    }

    private static async Task<Dictionary<string, string>> PerformCodeExchange(string code, string code_verifier, string redirectURI)
    {
      Dictionary<string, string> result = null;

      // builds the  request
      string tokenRequestBody = string.Format("code={0}&redirect_uri={1}&client_id={2}&code_verifier={3}&client_secret={4}&scope=&grant_type=authorization_code",
          code,
          System.Uri.EscapeDataString(redirectURI),
          CLIENT_ID,
          code_verifier,
          CLIENT_SECRET
          );

      // sends the request
      HttpWebRequest tokenRequest = (HttpWebRequest)WebRequest.Create(TOKEN_REQUEST_URI);
      tokenRequest.Method = "POST";
      tokenRequest.ContentType = "application/x-www-form-urlencoded";
      tokenRequest.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
      byte[] _byteVersion = System.Text.Encoding.ASCII.GetBytes(tokenRequestBody);
      tokenRequest.ContentLength = _byteVersion.Length;
      Stream stream = tokenRequest.GetRequestStream();
      await stream.WriteAsync(_byteVersion, 0, _byteVersion.Length);
      stream.Close();

      // gets the response
      WebResponse tokenResponse = await tokenRequest.GetResponseAsync();
      using (System.IO.StreamReader reader = new System.IO.StreamReader(tokenResponse.GetResponseStream()))
      {
        // reads response body
        string responseText = await reader.ReadToEndAsync();

        // converts to dictionary
        result = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);
      }

      return result;
    }

    private static async Task<Dictionary<string, string>> RefreshTokens()
    {
      Dictionary<string, string> result = null;

      // builds the  request
      string refreshRequestBody = string.Format("client_id={0}&client_secret={1}&refresh_token={2}&grant_type=refresh_token",
          CLIENT_ID,
          CLIENT_SECRET,
          tokens["refresh_token"]
      );

      // sends the request
      HttpWebRequest tokenRequest = (HttpWebRequest)WebRequest.Create(TOKEN_REQUEST_URI);
      tokenRequest.Method = "POST";
      tokenRequest.ContentType = "application/x-www-form-urlencoded";
      tokenRequest.Accept = "Accept=text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
      byte[] _byteVersion = System.Text.Encoding.ASCII.GetBytes(refreshRequestBody);
      tokenRequest.ContentLength = _byteVersion.Length;
      Stream stream = tokenRequest.GetRequestStream();
      await stream.WriteAsync(_byteVersion, 0, _byteVersion.Length);
      stream.Close();

      // gets the response
      WebResponse tokenResponse = await tokenRequest.GetResponseAsync();
      using (System.IO.StreamReader reader = new System.IO.StreamReader(tokenResponse.GetResponseStream()))
      {
        // reads response body
        string responseText = await reader.ReadToEndAsync();

        // converts to dictionary
        result = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseText);
      }

      return result;
    }

    private static int GetRandomUnusedPort()
    {
      var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var port = ((IPEndPoint)listener.LocalEndpoint).Port;
      listener.Stop();
      return port;
    }

    private static string randomDataBase64url(uint length)
    {
      RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
      byte[] bytes = new byte[length];
      rng.GetBytes(bytes);
      return base64urlencodeNoPadding(bytes);
    }

    private static byte[] sha256(string inputStirng)
    {
      byte[] bytes = System.Text.Encoding.ASCII.GetBytes(inputStirng);
      SHA256Managed sha256 = new SHA256Managed();
      return sha256.ComputeHash(bytes);
    }

    private static string base64urlencodeNoPadding(byte[] buffer)
    {
      string base64 = System.Convert.ToBase64String(buffer);

      // Converts base64 to base64url.
      base64 = base64.Replace("+", "-");
      base64 = base64.Replace("/", "_");
      // Strips padding.
      base64 = base64.Replace("=", "");

      return base64;
    }
  }
}
