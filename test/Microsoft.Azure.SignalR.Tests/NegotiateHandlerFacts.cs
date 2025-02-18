// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// From AspNetCore 3.0 preview7, there's a break change in HubConnectionContext
// which will break cross reference bettwen NETCOREAPP3.0 to NETStandard2.0 SDK
// So skip this part of UT when target 2.0 only
#if (MULTIFRAMEWORK)

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.SignalR.Tests
{
    public class NegotiateHandlerFacts
    {
        private const string CustomClaimType = "custom.claim";
        private const string CustomUserId = "customUserId";
        private const string DefaultUserId = "nameId";
        private const string DefaultConnectionString = "Endpoint=https://localhost;AccessKey=nOu3jXsHnsO5urMumc87M9skQbUWuQ+PE5IvSUEic8w=;";
        private const string ConnectionString2 = "Endpoint=http://localhost2;AccessKey=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;";
        private const string ConnectionString3 = "Endpoint=http://localhost3;AccessKey=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;";
        private const string ConnectionString4 = "Endpoint=http://localhost4;AccessKey=ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;";

        private static readonly JwtSecurityTokenHandler JwtSecurityTokenHandler = new JwtSecurityTokenHandler();

        [Theory]
        [InlineData(typeof(CustomUserIdProvider), CustomUserId)]
        [InlineData(typeof(NullUserIdProvider), null)]
        [InlineData(typeof(DefaultUserIdProvider), DefaultUserId)]
        public void GenerateNegotiateResponseWithUserId(Type type, string expectedUserId)
        {
            var config = new ConfigurationBuilder().Build();
            var serviceProvider = new ServiceCollection().AddSignalR()
                .AddAzureSignalR(
                o =>
                {
                    o.ConnectionString = DefaultConnectionString;
                    o.AccessTokenLifetime = TimeSpan.FromDays(1);
                })
                .Services
                .AddLogging()
                .AddSingleton<IConfiguration>(config)
                .AddSingleton(typeof(IUserIdProvider), type)
                .BuildServiceProvider();

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(CustomClaimType, CustomUserId),
                    new Claim(ClaimTypes.NameIdentifier, DefaultUserId),
                    new Claim("custom", "custom"),
                }))
            };

            var handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            var negotiateResponse = handler.Process(httpContext, "hub");

            Assert.NotNull(negotiateResponse);
            Assert.NotNull(negotiateResponse.Url);
            Assert.NotNull(negotiateResponse.AccessToken);
            Assert.Null(negotiateResponse.ConnectionId);
            Assert.Empty(negotiateResponse.AvailableTransports);

            var token = JwtSecurityTokenHandler.ReadJwtToken(negotiateResponse.AccessToken);
            Assert.Equal(expectedUserId, token.Claims.FirstOrDefault(x => x.Type == Constants.ClaimType.UserId)?.Value);
            Assert.Equal("custom", token.Claims.FirstOrDefault(x => x.Type == "custom")?.Value);
            Assert.Equal(TimeSpan.FromDays(1), token.ValidTo - token.ValidFrom);
            Assert.Null(token.Claims.FirstOrDefault(s => s.Type == Constants.ClaimType.ServerName));
            Assert.Null(token.Claims.FirstOrDefault(s => s.Type == Constants.ClaimType.ServerStickyMode));
        }

        [Fact]
        public void GenerateNegotiateResponseWithUserIdAndServerSticky()
        {
            var name = nameof(GenerateNegotiateResponseWithUserIdAndServerSticky);
            var serverNameProvider = new TestServerNameProvider(name);
            var config = new ConfigurationBuilder().Build();
            var serviceProvider = new ServiceCollection().AddSignalR()
                .AddAzureSignalR(
                o =>
                {
                    o.ServerStickyMode = ServerStickyMode.Required;
                    o.ConnectionString = DefaultConnectionString;
                    o.AccessTokenLifetime = TimeSpan.FromDays(1);
                })
                .Services
                .AddLogging()
                .AddSingleton<IConfiguration>(config)
                .AddSingleton(typeof(IUserIdProvider), typeof(DefaultUserIdProvider))
                .AddSingleton(typeof(IServerNameProvider), serverNameProvider)
                .BuildServiceProvider();

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(CustomClaimType, CustomUserId),
                    new Claim(ClaimTypes.NameIdentifier, DefaultUserId),
                    new Claim("custom", "custom"),
                }))
            };

            var handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            var negotiateResponse = handler.Process(httpContext, "hub");

            Assert.NotNull(negotiateResponse);
            Assert.NotNull(negotiateResponse.Url);
            Assert.NotNull(negotiateResponse.AccessToken);
            Assert.Null(negotiateResponse.ConnectionId);
            Assert.Empty(negotiateResponse.AvailableTransports);

            var token = JwtSecurityTokenHandler.ReadJwtToken(negotiateResponse.AccessToken);
            Assert.Equal(DefaultUserId, token.Claims.FirstOrDefault(x => x.Type == Constants.ClaimType.UserId)?.Value);
            Assert.Equal("custom", token.Claims.FirstOrDefault(x => x.Type == "custom")?.Value);
            Assert.Equal(TimeSpan.FromDays(1), token.ValidTo - token.ValidFrom);

            var serverName = token.Claims.FirstOrDefault(s => s.Type == Constants.ClaimType.ServerName)?.Value;
            Assert.Equal(name, serverName);
            var mode = token.Claims.FirstOrDefault(s => s.Type == Constants.ClaimType.ServerStickyMode)?.Value;
            Assert.Equal("Required", mode);
        }

        [Theory]
        [InlineData("/user/path/negotiate", "", "", "asrs.op=%2Fuser%2Fpath&asrs_request_id=")]
        [InlineData("/user/path/negotiate/", "", "a", "asrs.op=%2Fuser%2Fpath&asrs_request_id=a")]
        [InlineData("", "?customKey=customeValue", "?a=c", "customKey=customeValue&asrs_request_id=%3Fa%3Dc")]
        [InlineData("/user/path/negotiate", "?customKey=customeValue", "&", "asrs.op=%2Fuser%2Fpath&customKey=customeValue&asrs_request_id=%26")]
        public void GenerateNegotiateResponseWithPathAndQuery(string path, string queryString, string id, string expectedQueryString)
        {
            var requestIdProvider = new TestRequestIdProvider(id);
            var config = new ConfigurationBuilder().Build();
            var serviceProvider = new ServiceCollection().AddSignalR()
                .AddAzureSignalR(o => o.ConnectionString = DefaultConnectionString)
                .Services
                .AddLogging()
                .AddSingleton<IConfiguration>(config)
                .AddSingleton<IConnectionRequestIdProvider>(requestIdProvider)
                .BuildServiceProvider();

            var requestFeature = new HttpRequestFeature
            {
                Path = path,
                QueryString = queryString
            };
            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(requestFeature);
            var httpContext = new DefaultHttpContext(features);

            var handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            var negotiateResponse = handler.Process(httpContext, "chat");

            Assert.NotNull(negotiateResponse);
            Assert.EndsWith($"?hub=chat&{expectedQueryString}", negotiateResponse.Url);
        }

        [Theory]
        [InlineData("", "&", "?hub=chat&asrs_request_id=%26")]
        [InlineData("appName", "abc", "?hub=appname_chat&asrs_request_id=abc")]
        public void GenerateNegotiateResponseWithAppName(string appName, string id, string expectedResponse)
        {
            var requestIdProvider = new TestRequestIdProvider(id);
            var config = new ConfigurationBuilder().Build();
            var serviceProvider = new ServiceCollection().AddSignalR()
                .AddAzureSignalR(o =>
                {
                    o.ConnectionString = DefaultConnectionString;
                    o.ApplicationName = appName;
                })
                .Services
                .AddLogging()
                .AddSingleton<IConfiguration>(config)
                .AddSingleton<IConnectionRequestIdProvider>(requestIdProvider)
                .BuildServiceProvider();

            var requestFeature = new HttpRequestFeature
            {
            };
            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(requestFeature);
            var httpContext = new DefaultHttpContext(features);

            var handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            var negotiateResponse = handler.Process(httpContext, "chat");

            Assert.NotNull(negotiateResponse);
            Assert.EndsWith(expectedResponse, negotiateResponse.Url);
        }

        [Theory]
        [InlineData(typeof(ConnectionIdUserIdProvider), ServiceHubConnectionContext.ConnectionIdUnavailableError)]
        [InlineData(typeof(ConnectionAbortedTokenUserIdProvider), ServiceHubConnectionContext.ConnectionAbortedUnavailableError)]
        [InlineData(typeof(ItemsUserIdProvider), ServiceHubConnectionContext.ItemsUnavailableError)]
        [InlineData(typeof(ProtocolUserIdProvider), ServiceHubConnectionContext.ProtocolUnavailableError)]
        public void CustomUserIdProviderAccessUnavailablePropertyThrowsException(Type type, string errorMessage)
        {
            var config = new ConfigurationBuilder().Build();
            var serviceProvider = new ServiceCollection().AddSignalR()
                .AddAzureSignalR(o => o.ConnectionString = DefaultConnectionString)
                .Services
                .AddLogging()
                .AddSingleton<IConfiguration>(config)
                .AddSingleton(typeof(IUserIdProvider), type)
                .BuildServiceProvider();

            var handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal()
            };

            var exception = Assert.Throws<InvalidOperationException>(() => handler.Process(httpContext, "hub"));
            Assert.Equal(errorMessage, exception.Message);
        }

        [Fact]
        public void TestNegotiateHandlerWithMultipleEndpointsAndCustomerRouterAndAppName()
        {
            var requestIdProvider = new TestRequestIdProvider("a");
            var config = new ConfigurationBuilder().Build();
            var router = new TestCustomRouter();
            var serviceProvider = new ServiceCollection().AddSignalR()
                .AddAzureSignalR(o =>
                {
                    o.ApplicationName = "testprefix";
                    o.Endpoints = new ServiceEndpoint[]
                    {
                        new ServiceEndpoint(ConnectionString2),
                        new ServiceEndpoint(ConnectionString3, name: "chosen"),
                        new ServiceEndpoint(ConnectionString4),
                    };
                })
                .Services
                .AddLogging()
                .AddSingleton<IEndpointRouter>(router)
                .AddSingleton<IConfiguration>(config)
                .AddSingleton<IConnectionRequestIdProvider>(requestIdProvider)
                .BuildServiceProvider();

            var requestFeature = new HttpRequestFeature
            {
                Path = "/user/path/negotiate/",
                QueryString = "?endpoint=chosen"
            };

            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(requestFeature);
            var httpContext = new DefaultHttpContext(features);

            var handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            var negotiateResponse = handler.Process(httpContext, "chat");

            Assert.NotNull(negotiateResponse);
            Assert.Equal($"http://localhost3/client/?hub=testprefix_chat&asrs.op=%2Fuser%2Fpath&endpoint=chosen&asrs_request_id=a", negotiateResponse.Url);
        }

        [Fact]
        public void TestNegotiateHandlerWithMultipleEndpointsAndCustomRouter()
        {
            var requestIdProvider = new TestRequestIdProvider("a");
            var config = new ConfigurationBuilder().Build();
            var router = new TestCustomRouter();
            var serviceProvider = new ServiceCollection().AddSignalR()
                .AddAzureSignalR(
                o => o.Endpoints = new ServiceEndpoint[]
                {
                    new ServiceEndpoint(ConnectionString2),
                    new ServiceEndpoint(ConnectionString3, name: "chosen"),
                    new ServiceEndpoint(ConnectionString4),
                })
                .Services
                .AddLogging()
                .AddSingleton<IEndpointRouter>(router)
                .AddSingleton<IConfiguration>(config)
                .AddSingleton<IConnectionRequestIdProvider>(requestIdProvider)
                .BuildServiceProvider();

            var requestFeature = new HttpRequestFeature
            {
                Path = "/user/path/negotiate/",
                QueryString = "?endpoint=chosen"
            };
            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(requestFeature);
            var httpContext = new DefaultHttpContext(features);

            var handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            var negotiateResponse = handler.Process(httpContext, "chat");

            Assert.NotNull(negotiateResponse);
            Assert.Equal($"http://localhost3/client/?hub=chat&asrs.op=%2Fuser%2Fpath&endpoint=chosen&asrs_request_id=a", negotiateResponse.Url);

            // With no query string should return 400
            requestFeature = new HttpRequestFeature
            {
                Path = "/user/path/negotiate/",
            };

            var responseFeature = new HttpResponseFeature();
            features.Set<IHttpRequestFeature>(requestFeature);
            features.Set<IHttpResponseFeature>(responseFeature);
            httpContext = new DefaultHttpContext(features);

            handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            negotiateResponse = handler.Process(httpContext, "chat");

            Assert.Null(negotiateResponse);

            Assert.Equal(400, responseFeature.StatusCode);

            // With no query string should return 400
            requestFeature = new HttpRequestFeature
            {
                Path = "/user/path/negotiate/",
                QueryString = "?endpoint=notexists"
            };

            responseFeature = new HttpResponseFeature();
            features.Set<IHttpRequestFeature>(requestFeature);
            features.Set<IHttpResponseFeature>(responseFeature);
            httpContext = new DefaultHttpContext(features);

            handler = serviceProvider.GetRequiredService<NegotiateHandler>();
            Assert.Throws<InvalidOperationException>(() => handler.Process(httpContext, "chat"));
        }

        private sealed class TestServerNameProvider : IServerNameProvider
        {
            private readonly string _serverName;
            public TestServerNameProvider(string serverName)
            {
                _serverName = serverName;
            }

            public string GetName()
            {
                return _serverName;
            }
        }

        private class TestCustomRouter : EndpointRouterDecorator
        {
            public override ServiceEndpoint GetNegotiateEndpoint(HttpContext context, IEnumerable<ServiceEndpoint> endpoints)
            {
                var endpointName = context.Request.Query["endpoint"];
                if (endpointName.Count == 0)
                {
                    context.Response.StatusCode = 400;
                    var response = Encoding.UTF8.GetBytes("Invalid request");
                    // In latest DefaultHttpContext, response body is set to null
                    context.Response.Body = new MemoryStream();
                    context.Response.Body.Write(response, 0, response.Length);
                    return null;
                }

                return endpoints.First(s => s.Name == endpointName && s.Online);
            }
        }

        private class CustomUserIdProvider : IUserIdProvider
        {
            public string GetUserId(HubConnectionContext connection)
            {
                return connection.GetHttpContext()?.User?.Claims?.First(c => c.Type == CustomClaimType)?.Value;
            }
        }

        private class NullUserIdProvider : IUserIdProvider
        {
            public string GetUserId(HubConnectionContext connection) => null;
        }

        private class ConnectionIdUserIdProvider : IUserIdProvider
        {
            public string GetUserId(HubConnectionContext connection) => connection.ConnectionId;
        }

        private class ConnectionAbortedTokenUserIdProvider : IUserIdProvider
        {
            public string GetUserId(HubConnectionContext connection) => connection.ConnectionAborted.IsCancellationRequested.ToString();
        }

        private class ItemsUserIdProvider : IUserIdProvider
        {
            public string GetUserId(HubConnectionContext connection) => connection.Items.ToString();
        }

        private class ProtocolUserIdProvider : IUserIdProvider
        {
            public string GetUserId(HubConnectionContext connection) => connection.Protocol.Name;
        }
    }
}

#endif