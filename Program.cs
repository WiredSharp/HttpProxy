
using Microsoft.Extensions.Primitives;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

static async Task processRequest(HttpContext context)
{
   context.Response.ContentType = "application/json";
   if (!context.Request.Headers.ContainsKey("Authorization") || (context.Request.Headers["Authorization"][0]?.StartsWith("Bearer") ?? true))
   {
      context.Response.StatusCode = 401;
      await context.Response.WriteAsJsonAsync(new { message = "The request is not authenticated" });
   }
   try
   {
      IEnumerable<HttpRequestMessage> forwardRequests = BuildForwardRequests(context.Request);
      var client = new HttpClient();
      HttpResponseMessage[] responses = await Task.WhenAll(forwardRequests.Select(request => client.SendAsync(request)).ToArray());
      string[] contents = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
      context.Response.StatusCode = 200;
      await context.Response.WriteAsJsonAsync(contents);
   }
   catch (ArgumentException ex)
   {
      context.Response.StatusCode = 400;
      await context.Response.WriteAsJsonAsync(new { error = ex.Message });
   }
}

static IEnumerable<QueryString> BuildQueryStrings(IQueryCollection query)
{
   KeyValuePair<string, StringValues>[] parameters = query.Where(x => x.Value.Count > 1).ToArray();
   if (parameters.Length > 1)
   {
      throw new ArgumentException($"The request has more than one duplicated query parameter ({parameters.Select(x => x.Key)}");
   }
   if (parameters.Length == 0)
   {
      return [QueryString.Create(query)];
   }
   else
   {
      QueryString[] queryStrings = new QueryString[parameters[0].Value.Count];
      for (int i = 0; i < queryStrings.Length; ++i)
      {
         queryStrings[i] = new QueryString();
         queryStrings[i].Add(parameters[0].Key, parameters[0].Value[i] ?? "");
      }
      foreach (var parameter in query)
      {
         if (parameter.Key != parameters[0].Key)
         {
            foreach (var newQuery in queryStrings)
            {
               newQuery.Add(parameter.Key, parameter.Value[0] ?? "");
            }
         }
      }
      return queryStrings;
   }
}

static IEnumerable<HttpRequestMessage> BuildForwardRequests(HttpRequest request)
{
   KeyValuePair<string, StringValues>[] parameters = request.Query.Where(x => x.Value.Count > 1).ToArray();
   if (parameters.Length > 1)
   {
      throw new ArgumentException($"The request has more than one duplicated query parameter ({parameters.Select(x => x.Key)}");
   }
   var forwardUrl = new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? 80, request.Path);
   foreach (QueryString queryString in BuildQueryStrings(request.Query))
   {
      forwardUrl.Query = queryString.ToString();
      var forwardRequest = new HttpRequestMessage(new HttpMethod(request.Method), forwardUrl.Uri);
      foreach (var header in request.Headers)
      {
         forwardRequest.Headers.Add(header.Key, header.Value.ToArray());
      }
      forwardRequest.Content = new StreamContent(request.Body);
      yield return forwardRequest;
   }
}

if (app.Environment.IsDevelopment())
{
   app.Logger.LogDebug("The app is in development mode");
   app.UseDeveloperExceptionPage();
}

app.Logger.LogInformation("The app started");

// app.Use((RequestDelegate next) => {
//       return async context => {
//          await processRequest(context);
//          await next(context);
//       };
//    });

app.Run(processRequest);

app.Run();

// app.Run(context =>
// {
//     context.Response.StatusCode = 404;
//     return Task.CompletedTask;
// });

app.Logger.LogInformation("The app has stopped");