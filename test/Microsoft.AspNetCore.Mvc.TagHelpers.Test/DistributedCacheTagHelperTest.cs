// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.WebEncoders.Testing;
using Moq;
using Xunit;

namespace Microsoft.AspNetCore.Mvc.TagHelpers
{
    public class DistributedCacheTagHelperTest
    {
        [Fact]
        public void GenerateKey_ReturnsKeyBasedOnTagHelperName()
        {
            // Arrange
            var name = "some-name";
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(), 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                Name = name
            };
            var expected = GetHashedBytes("DistributedCacheTagHelper||" + name);

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(expected, key);
        }

        [Theory]
        [InlineData("Vary-By-Value")]
        [InlineData("Vary  with spaces")]
        [InlineData("  Vary  with more spaces   ")]
        public void GenerateKey_UsesVaryByPropertyToGenerateKey(string varyBy)
        {
            // Arrange
            var name = "some-name";
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(), 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                VaryBy = varyBy,
                Name = name
            };
            var expected = GetHashedBytes("DistributedCacheTagHelper||some-name||VaryBy||" + varyBy);

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(expected, key);
        }

        [Theory]
        [InlineData("Cookie0", "DistributedCacheTagHelper||some-name||VaryByCookie(Cookie0||Cookie0Value)")]
        [InlineData("Cookie0,Cookie1",
            "DistributedCacheTagHelper||some-name||VaryByCookie(Cookie0||Cookie0Value||Cookie1||Cookie1Value)")]
        [InlineData("Cookie0, Cookie1",
            "DistributedCacheTagHelper||some-name||VaryByCookie(Cookie0||Cookie0Value||Cookie1||Cookie1Value)")]
        [InlineData("   Cookie0,   ,   Cookie1   ",
            "DistributedCacheTagHelper||some-name||VaryByCookie(Cookie0||Cookie0Value||Cookie1||Cookie1Value)")]
        [InlineData(",Cookie0,,Cookie1,",
            "DistributedCacheTagHelper||some-name||VaryByCookie(Cookie0||Cookie0Value||Cookie1||Cookie1Value)")]
        public void GenerateKey_UsesVaryByCookieName(string varyByCookie, string expected)
        {
            // Arrange
            var name = "some-name";
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(), 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                VaryByCookie = varyByCookie,
                Name = name
            };
            cacheTagHelper.ViewContext.HttpContext.Request.Headers["Cookie"] =
                "Cookie0=Cookie0Value;Cookie1=Cookie1Value";

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(GetHashedBytes(expected), key);
        }

        [Theory]
        [InlineData("Accept-Language", "DistributedCacheTagHelper||some-name||VaryByHeader(Accept-Language||en-us;charset=utf8)")]
        [InlineData("X-CustomHeader,Accept-Encoding, NotAvailable",
            "DistributedCacheTagHelper||some-name||VaryByHeader(X-CustomHeader||Header-Value||Accept-Encoding||utf8||NotAvailable||)")]
        [InlineData("X-CustomHeader,  , Accept-Encoding, NotAvailable",
            "DistributedCacheTagHelper||some-name||VaryByHeader(X-CustomHeader||Header-Value||Accept-Encoding||utf8||NotAvailable||)")]
        public void GenerateKey_UsesVaryByHeader(string varyByHeader, string expected)
        {
            // Arrange
            var name = "some-name";
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(), 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                VaryByHeader = varyByHeader,
                Name = name
            };
            var headers = cacheTagHelper.ViewContext.HttpContext.Request.Headers;
            headers["Accept-Language"] = "en-us;charset=utf8";
            headers["Accept-Encoding"] = "utf8";
            headers["X-CustomHeader"] = "Header-Value";

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(GetHashedBytes(expected), key);
        }

        [Theory]
        [InlineData("category", "DistributedCacheTagHelper||some-name||VaryByQuery(category||cats)")]
        [InlineData("Category,SortOrder,SortOption",
            "DistributedCacheTagHelper||some-name||VaryByQuery(Category||cats||SortOrder||||SortOption||Adorability)")]
        [InlineData("Category,  SortOrder, SortOption,  ",
            "DistributedCacheTagHelper||some-name||VaryByQuery(Category||cats||SortOrder||||SortOption||Adorability)")]
        public void GenerateKey_UsesVaryByQuery(string varyByQuery, string expected)
        {
            // Arrange
            var name = "some-name";
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(), 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                VaryByQuery = varyByQuery,
                Name = name
            };
            cacheTagHelper.ViewContext.HttpContext.Request.QueryString =
                new QueryString("?sortoption=Adorability&Category=cats&sortOrder=");

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(GetHashedBytes(expected), key);
        }

        [Theory]
        [InlineData("id", "DistributedCacheTagHelper||some-name||VaryByRoute(id||4)")]
        [InlineData("Category,,Id,OptionRouteValue",
            "DistributedCacheTagHelper||some-name||VaryByRoute(Category||MyCategory||Id||4||OptionRouteValue||)")]
        [InlineData(" Category,  , Id,   OptionRouteValue,   ",
            "DistributedCacheTagHelper||some-name||VaryByRoute(Category||MyCategory||Id||4||OptionRouteValue||)")]
        public void GenerateKey_UsesVaryByRoute(string varyByRoute, string expected)
        {
            // Arrange
            var name = "some-name";
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(), 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                VaryByRoute = varyByRoute,
                Name = name
            };
            cacheTagHelper.ViewContext.RouteData.Values["id"] = 4;
            cacheTagHelper.ViewContext.RouteData.Values["category"] = "MyCategory";

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(GetHashedBytes(expected), key);
        }

        [Fact]
        public void GenerateKey_UsesVaryByUser_WhenUserIsNotAuthenticated()
        {
            // Arrange
            var name = "some-name";
            var expected = "DistributedCacheTagHelper||some-name||VaryByUser||";
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(),
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                VaryByUser = true,
                Name = name
            };

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(GetHashedBytes(expected), key);
        }

        [Fact]
        public void GenerateKey_UsesVaryByUserAndAuthenticatedUserName()
        {
            // Arrange
            var name = "some-name";
            var expected = "DistributedCacheTagHelper||some-name||VaryByUser||test_name";
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(),
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                VaryByUser = true,
                Name = name
            };
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimsIdentity.DefaultNameClaimType, "test_name") });
            cacheTagHelper.ViewContext.HttpContext.User = new ClaimsPrincipal(identity);

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(GetHashedBytes(expected), key);
        }

        [Fact]
        public void GenerateKey_WithMultipleVaryByOptions_CreatesCombinedKey()
        {
            // Arrange
            var name = "some-name";
            var expected = GetHashedBytes("DistributedCacheTagHelper||some-name||VaryBy||custom-value||" +
                "VaryByHeader(content-type||text/html)||VaryByUser||someuser");
            var tagHelperContext = GetTagHelperContext();
            var cacheTagHelper = new DistributedCacheTagHelper(
                Mock.Of<IDistributedCacheTagHelperService>(),
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                VaryByUser = true,
                VaryByHeader = "content-type",
                VaryBy = "custom-value",
                Name = name
            };
            cacheTagHelper.ViewContext.HttpContext.Request.Headers["Content-Type"] = "text/html";
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimsIdentity.DefaultNameClaimType, "someuser") });
            cacheTagHelper.ViewContext.HttpContext.User = new ClaimsPrincipal(identity);

            // Act
            var key = cacheTagHelper.GenerateKey(tagHelperContext);

            // Assert
            Assert.Equal(expected, key);
        }

        [Fact]
        public async Task ProcessAsync_DoesNotCache_IfDisabled()
        {
            // Arrange
            var childContent = "original-child-content";
            var storage = new Mock<IDistributedCacheTagHelperStorage>();
            var value = Encoding.UTF8.GetBytes("ok");
            storage.Setup(c => c.SetAsync(
                /*key*/ It.IsAny<string>(),
                /*value*/ value,
                /*options*/ It.IsAny<DistributedCacheEntryOptions>()));
            storage.Setup(c => c.GetAsync(It.IsAny<string>()))
                .Returns(Task.FromResult(value));
            var tagHelperContext = GetTagHelperContext();
            var service = new DistributedCacheTagHelperService(
                storage.Object,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder());
            var tagHelperOutput = GetTagHelperOutput(
                attributes: new TagHelperAttributeList(),
                childContent: childContent);
            var cacheTagHelper = new DistributedCacheTagHelper(
                service,
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                Enabled = false
            };

            // Act
            await cacheTagHelper.ProcessAsync(tagHelperContext, tagHelperOutput);

            // Assert
            Assert.Equal(childContent, tagHelperOutput.Content.GetContent());
            storage.Verify(c => c.SetAsync(
                /*key*/ It.IsAny<string>(),
                /*content*/ value,
                /*options*/ It.IsAny<DistributedCacheEntryOptions>()),
                Times.Never);
        }

        [Fact]
        public async Task ProcessAsync_ReturnsCachedValue_IfEnabled()
        {
            // Arrange
            var childContent = "original-child-content";
            var storage = new Mock<IDistributedCacheTagHelperStorage>();
            var value = Encoding.UTF8.GetBytes(childContent);
            storage.Setup(c => c.SetAsync(
                /*key*/ It.IsAny<string>(),
                /*value*/ value,
                /*options*/ It.IsAny<DistributedCacheEntryOptions>()));
            storage.Setup(c => c.GetAsync(It.IsAny<string>()))
                .Returns(Task.FromResult<byte[]>(null));
            var service = new DistributedCacheTagHelperService(
                storage.Object,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder());
            var tagHelperContext = GetTagHelperContext();
            var tagHelperOutput = GetTagHelperOutput(
                attributes: new TagHelperAttributeList(),
                childContent: childContent);
            var cacheTagHelper = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                Enabled = true,
                Name = "some-name"
            };

            // Act
            await cacheTagHelper.ProcessAsync(tagHelperContext, tagHelperOutput);

            // Assert
            Assert.Empty(tagHelperOutput.PreContent.GetContent());
            Assert.Empty(tagHelperOutput.PostContent.GetContent());
            Assert.True(tagHelperOutput.IsContentModified);
            Assert.Equal(childContent, tagHelperOutput.Content.GetContent());

            storage.Verify(c => c.GetAsync(
                /*key*/ It.IsAny<string>()
                ),
                Times.Once);
                
            storage.Verify(c => c.SetAsync(
                /*key*/ It.IsAny<string>(),
                /*value*/ It.IsAny<byte[]>(),
                /*options*/ It.IsAny<DistributedCacheEntryOptions>()),
                Times.Once);

        }

        [Fact]
        public async Task ProcessAsync_ReturnsCachedValue_IfVaryByParamIsUnchanged()
        {
            // Arrange - 1
            var childContent = "original-child-content";
            var storage = GetStorage();
            var formatter = GetFormatter();
            var tagHelperContext1 = GetTagHelperContext();
            var tagHelperOutput1 = GetTagHelperOutput(
                attributes: new TagHelperAttributeList(),
                childContent: childContent);
            var service = new DistributedCacheTagHelperService(
                storage,
                formatter,
                new HtmlTestEncoder());
            var cacheTagHelper1 = new DistributedCacheTagHelper(
                service,
                new HtmlTestEncoder())
            {
                Enabled = true,
                VaryByQuery = "key1,key2",
                ViewContext = GetViewContext(),
            };
            cacheTagHelper1.ViewContext.HttpContext.Request.QueryString = new QueryString(
                "?key1=value1&key2=value2");

            // Act - 1
            await cacheTagHelper1.ProcessAsync(tagHelperContext1, tagHelperOutput1);

            // Assert - 1
            Assert.Empty(tagHelperOutput1.PreContent.GetContent());
            Assert.Empty(tagHelperOutput1.PostContent.GetContent());
            Assert.True(tagHelperOutput1.IsContentModified);
            Assert.Equal(childContent, tagHelperOutput1.Content.GetContent());

            // Arrange - 2
            var tagHelperContext2 = GetTagHelperContext();
            var tagHelperOutput2 = GetTagHelperOutput(
                attributes: new TagHelperAttributeList(),
                childContent: "different-content");
            var cacheTagHelper2 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                Enabled = true,
                VaryByQuery = "key1,key2",
                ViewContext = GetViewContext(),
            };
            cacheTagHelper2.ViewContext.HttpContext.Request.QueryString = new QueryString(
                "?key1=value1&key2=value2");

            // Act - 2
            await cacheTagHelper2.ProcessAsync(tagHelperContext2, tagHelperOutput2);

            // Assert - 2
            Assert.Empty(tagHelperOutput2.PreContent.GetContent());
            Assert.Empty(tagHelperOutput2.PostContent.GetContent());
            Assert.True(tagHelperOutput2.IsContentModified);
            Assert.Equal(childContent, tagHelperOutput2.Content.GetContent());
        }

        [Fact]
        public async Task ProcessAsync_RecalculatesValueIfCacheKeyChanges()
        {
            // Arrange - 1
            var childContent1 = "original-child-content";
            var storage = GetStorage();
            var service = new DistributedCacheTagHelperService(
                storage,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder());
            var tagHelperContext1 = GetTagHelperContext();
            var tagHelperOutput1 = GetTagHelperOutput(childContent: childContent1);
            tagHelperOutput1.PreContent.Append("<cache>");
            tagHelperOutput1.PostContent.SetContent("</cache>");
            var cacheTagHelper1 = new DistributedCacheTagHelper(
                service,
                new HtmlTestEncoder())
            {
                VaryByCookie = "cookie1,cookie2",
                ViewContext = GetViewContext(),
            };
            cacheTagHelper1.ViewContext.HttpContext.Request.Headers["Cookie"] = "cookie1=value1;cookie2=value2";

            // Act - 1
            await cacheTagHelper1.ProcessAsync(tagHelperContext1, tagHelperOutput1);

            // Assert - 1
            Assert.Empty(tagHelperOutput1.PreContent.GetContent());
            Assert.Empty(tagHelperOutput1.PostContent.GetContent());
            Assert.True(tagHelperOutput1.IsContentModified);
            Assert.Equal(childContent1, tagHelperOutput1.Content.GetContent());

            // Arrange - 2
            var childContent2 = "different-content";
            var tagHelperContext2 = GetTagHelperContext();
            var tagHelperOutput2 = GetTagHelperOutput(childContent: childContent2);
            tagHelperOutput2.PreContent.SetContent("<cache>");
            tagHelperOutput2.PostContent.SetContent("</cache>");
            var cacheTagHelper2 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                VaryByCookie = "cookie1,cookie2",
                ViewContext = GetViewContext(),
            };
            cacheTagHelper2.ViewContext.HttpContext.Request.Headers["Cookie"] = "cookie1=value1;cookie2=not-value2";

            // Act - 2
            await cacheTagHelper2.ProcessAsync(tagHelperContext2, tagHelperOutput2);

            // Assert - 2
            Assert.Empty(tagHelperOutput2.PreContent.GetContent());
            Assert.Empty(tagHelperOutput2.PostContent.GetContent());
            Assert.True(tagHelperOutput2.IsContentModified);
            Assert.Equal(childContent2, tagHelperOutput2.Content.GetContent());
        }

        [Fact]
        public void UpdateCacheEntryOptions_SetsAbsoluteExpiration_IfExpiresOnIsSet()
        {
            // Arrange
            var expiresOn = DateTimeOffset.UtcNow.AddMinutes(4);
            var storage = GetStorage();
            var service = new DistributedCacheTagHelperService(
                storage,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder()
                );
            var cacheTagHelper = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ExpiresOn = expiresOn
            };

            // Act
            var cacheEntryOptions = cacheTagHelper.GetDistributedCacheEntryOptions();

            // Assert
            Assert.Equal(expiresOn, cacheEntryOptions.AbsoluteExpiration);
        }

        [Fact]
        public void UpdateCacheEntryOptions_SetsAbsoluteExpiration_IfExpiresAfterIsSet()
        {
            // Arrange
            var expiresAfter = TimeSpan.FromSeconds(42);
            var storage = GetStorage();
            var service = new DistributedCacheTagHelperService(
                storage,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder()
                );
            var cacheTagHelper = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ExpiresAfter = expiresAfter
            };

            // Act
            var cacheEntryOptions = cacheTagHelper.GetDistributedCacheEntryOptions();

            // Assert
            Assert.Equal(expiresAfter, cacheEntryOptions.AbsoluteExpirationRelativeToNow);
        }

        [Fact]
        public void UpdateCacheEntryOptions_SetsSlidingExpiration_IfExpiresSlidingIsSet()
        {
            // Arrange
            var expiresSliding = TimeSpan.FromSeconds(37);
            var storage = GetStorage();
            var service = new DistributedCacheTagHelperService(
                storage,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder()
                );
            var cacheTagHelper = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ExpiresSliding = expiresSliding
            };

            // Act
            var cacheEntryOptions = cacheTagHelper.GetDistributedCacheEntryOptions();

            // Assert
            Assert.Equal(expiresSliding, cacheEntryOptions.SlidingExpiration);
        }
        
        [Fact]
        public async Task ProcessAsync_UsesExpiresAfter_ToExpireCacheEntry()
        {
            // Arrange - 1
            var currentTime = new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var childContent1 = "original-child-content";
            var clock = new Mock<ISystemClock>();
            clock.SetupGet(p => p.UtcNow)
                .Returns(() => currentTime);
            var storage = GetStorage(new MemoryCacheOptions { Clock = clock.Object });
            var service = new DistributedCacheTagHelperService(
                storage,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder()
                );
            var tagHelperContext1 = GetTagHelperContext();
            var tagHelperOutput1 = GetTagHelperOutput(childContent: childContent1);
            tagHelperOutput1.PreContent.SetContent("<cache>");
            tagHelperOutput1.PostContent.SetContent("</cache>");
            var cacheTagHelper1 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                ExpiresAfter = TimeSpan.FromMinutes(10)
            };

            // Act - 1
            await cacheTagHelper1.ProcessAsync(tagHelperContext1, tagHelperOutput1);

            // Assert - 1
            Assert.Empty(tagHelperOutput1.PreContent.GetContent());
            Assert.Empty(tagHelperOutput1.PostContent.GetContent());
            Assert.True(tagHelperOutput1.IsContentModified);
            Assert.Equal(childContent1, tagHelperOutput1.Content.GetContent());

            // Arrange - 2
            var childContent2 = "different-content";
            var tagHelperContext2 = GetTagHelperContext();
            var tagHelperOutput2 = GetTagHelperOutput(childContent: childContent2);
            tagHelperOutput2.PreContent.SetContent("<cache>");
            tagHelperOutput2.PostContent.SetContent("</cache>");
            var cacheTagHelper2 = new DistributedCacheTagHelper(
                service,
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                ExpiresAfter = TimeSpan.FromMinutes(10)
            };
            currentTime = currentTime.AddMinutes(11);

            // Act - 2
            await cacheTagHelper2.ProcessAsync(tagHelperContext2, tagHelperOutput2);

            // Assert - 2
            Assert.Empty(tagHelperOutput2.PreContent.GetContent());
            Assert.Empty(tagHelperOutput2.PostContent.GetContent());
            Assert.True(tagHelperOutput2.IsContentModified);
            Assert.Equal(childContent2, tagHelperOutput2.Content.GetContent());
        }

        [Fact]
        public async Task ProcessAsync_UsesExpiresOn_ToExpireCacheEntry()
        {
            // Arrange - 1
            var currentTime = new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var childContent1 = "original-child-content";
            var clock = new Mock<ISystemClock>();
            clock.SetupGet(p => p.UtcNow)
                .Returns(() => currentTime);
            var storage = GetStorage(new MemoryCacheOptions { Clock = clock.Object });
            var service = new DistributedCacheTagHelperService(
                storage,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder()
                );
            var tagHelperContext1 = GetTagHelperContext();
            var tagHelperOutput1 = GetTagHelperOutput(childContent: childContent1);
            tagHelperOutput1.PreContent.SetContent("<distributed-cache>");
            tagHelperOutput1.PostContent.SetContent("</distributed-cache>");
            var cacheTagHelper1 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                ExpiresOn = currentTime.AddMinutes(5)
            };

            // Act - 1
            await cacheTagHelper1.ProcessAsync(tagHelperContext1, tagHelperOutput1);

            // Assert - 1
            Assert.Empty(tagHelperOutput1.PreContent.GetContent());
            Assert.Empty(tagHelperOutput1.PostContent.GetContent());
            Assert.True(tagHelperOutput1.IsContentModified);
            Assert.Equal(childContent1, tagHelperOutput1.Content.GetContent());

            // Arrange - 2
            currentTime = currentTime.AddMinutes(5).AddSeconds(2);
            var childContent2 = "different-content";
            var tagHelperContext2 = GetTagHelperContext();
            var tagHelperOutput2 = GetTagHelperOutput(childContent: childContent2);
            tagHelperOutput2.PreContent.SetContent("<distributed-cache>");
            tagHelperOutput2.PostContent.SetContent("</distributed-cache>");
            var cacheTagHelper2 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                ExpiresOn = currentTime.AddMinutes(5)
            };

            // Act - 2
            await cacheTagHelper2.ProcessAsync(tagHelperContext2, tagHelperOutput2);

            // Assert - 2
            Assert.Empty(tagHelperOutput2.PreContent.GetContent());
            Assert.Empty(tagHelperOutput2.PostContent.GetContent());
            Assert.True(tagHelperOutput2.IsContentModified);
            Assert.Equal(childContent2, tagHelperOutput2.Content.GetContent());
        }

        [Fact]
        public async Task ProcessAsync_UsesExpiresSliding_ToExpireCacheEntryWithSlidingExpiration()
        {
            // Arrange - 1
            var currentTime = new DateTimeOffset(2010, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var childContent1 = "original-child-content";
            var clock = new Mock<ISystemClock>();
            clock.SetupGet(p => p.UtcNow)
                .Returns(() => currentTime);
            var storage = GetStorage(new MemoryCacheOptions { Clock = clock.Object });
            var service = new DistributedCacheTagHelperService(
                storage,
                Mock.Of<IDistributedCacheTagHelperFormatter>(),
                new HtmlTestEncoder()
                );
            var tagHelperContext1 = GetTagHelperContext();
            var tagHelperOutput1 = GetTagHelperOutput(childContent: childContent1);
            tagHelperOutput1.PreContent.SetContent("<distributed-cache>");
            tagHelperOutput1.PostContent.SetContent("</distributed-cache>");
            var cacheTagHelper1 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                ExpiresSliding = TimeSpan.FromSeconds(30)
            };

            // Act - 1
            await cacheTagHelper1.ProcessAsync(tagHelperContext1, tagHelperOutput1);

            // Assert - 1
            Assert.Empty(tagHelperOutput1.PreContent.GetContent());
            Assert.Empty(tagHelperOutput1.PostContent.GetContent());
            Assert.True(tagHelperOutput1.IsContentModified);
            Assert.Equal(childContent1, tagHelperOutput1.Content.GetContent());

            // Arrange - 2
            currentTime = currentTime.AddSeconds(35);
            var childContent2 = "different-content";
            var tagHelperContext2 = GetTagHelperContext();
            var tagHelperOutput2 = GetTagHelperOutput(childContent: childContent2);
            tagHelperOutput2.PreContent.SetContent("<distributed-cache>");
            tagHelperOutput2.PostContent.SetContent("</distributed-cache>");
            var cacheTagHelper2 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                ExpiresSliding = TimeSpan.FromSeconds(30)
            };

            // Act - 2
            await cacheTagHelper2.ProcessAsync(tagHelperContext2, tagHelperOutput2);

            // Assert - 2
            Assert.Empty(tagHelperOutput2.PreContent.GetContent());
            Assert.Empty(tagHelperOutput2.PostContent.GetContent());
            Assert.True(tagHelperOutput2.IsContentModified);
            Assert.Equal(childContent2, tagHelperOutput2.Content.GetContent());
        }

        [Fact]
        public async Task ProcessAsync_ComputesValueOnce_WithConcurrentRequests()
        {
            // Arrange
            var childContent = "some-content";
            var resetEvent1 = new ManualResetEvent(false);
            var resetEvent2 = new ManualResetEvent(false);
            var calls = 0;
            var formatter = GetFormatter();
            var storage = GetStorage();
            var service = new DistributedCacheTagHelperService(
                storage,
                formatter,
                new HtmlTestEncoder()
                );
            var tagHelperContext1 = GetTagHelperContext();
            var tagHelperContext2 = GetTagHelperContext();

            TagHelperOutput tagHelperOutput = new TagHelperOutput(
                "distributed-cache",
                new TagHelperAttributeList(),
                getChildContentAsync: (useCachedResult, encoder) =>
                {
                    calls++;
                    resetEvent2.Set();

                    var tagHelperContent = new DefaultTagHelperContent();
                    tagHelperContent.SetHtmlContent(childContent);
                    return Task.FromResult<TagHelperContent>(tagHelperContent);
                });

            var cacheTagHelper1 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                Enabled = true
            };

            var cacheTagHelper2 = new DistributedCacheTagHelper(
                service, 
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                Enabled = true
            };

            // Act

            var task1 = Task.Run(async () =>
            {
                resetEvent1.WaitOne(5000);
                await cacheTagHelper1.ProcessAsync(tagHelperContext1, tagHelperOutput);
            });

            var task2 = Task.Run(async () =>
            {
                resetEvent2.WaitOne(5000);
                await cacheTagHelper2.ProcessAsync(tagHelperContext1, tagHelperOutput);
            });

            resetEvent1.Set();
            await Task.WhenAll(task1, task2);

            // Assert
            Assert.Empty(tagHelperOutput.PreContent.GetContent());
            Assert.Empty(tagHelperOutput.PostContent.GetContent());
            Assert.True(tagHelperOutput.IsContentModified);
            Assert.Equal(childContent, tagHelperOutput.Content.GetContent());

            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task ProcessAsync_ExceptionInProcessing_DoesntBlockConcurrentRequests()
        {
            // Arrange
            var childContent = "some-content";
            var resetEvent1 = new ManualResetEvent(false);
            var resetEvent2 = new ManualResetEvent(false);
            var calls = 0;
            var formatter = GetFormatter();
            var storage = GetStorage();
            var service = new DistributedCacheTagHelperService(
                storage,
                formatter,
                new HtmlTestEncoder()
                );
            var tagHelperContext1 = GetTagHelperContext();
            var tagHelperContext2 = GetTagHelperContext();

            var tagHelperOutput1 = new TagHelperOutput(
                "distributed-cache",
                new TagHelperAttributeList(),
                getChildContentAsync: (useCachedResult, encoder) =>
                {
                    calls++;
                    resetEvent2.Set();

                    throw new Exception();
                });

            var tagHelperOutput2 = new TagHelperOutput(
                "distributed-cache",
                new TagHelperAttributeList(),
                getChildContentAsync: (useCachedResult, encoder) =>
                {
                    calls++;

                    var tagHelperContent = new DefaultTagHelperContent();
                    tagHelperContent.SetHtmlContent(childContent);
                    return Task.FromResult<TagHelperContent>(tagHelperContent);
                });

            var cacheTagHelper1 = new DistributedCacheTagHelper(
                service,
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                Enabled = true
            };

            var cacheTagHelper2 = new DistributedCacheTagHelper(
                service,
                new HtmlTestEncoder())
            {
                ViewContext = GetViewContext(),
                Enabled = true
            };

            // Act

            var task1 = Task.Run(async () =>
            {
                resetEvent1.WaitOne(5000);
                await Assert.ThrowsAsync<Exception>(() => cacheTagHelper1.ProcessAsync(tagHelperContext1, tagHelperOutput1));
            });

            var task2 = Task.Run(async () =>
            {
                resetEvent2.WaitOne(5000);
                await cacheTagHelper2.ProcessAsync(tagHelperContext2, tagHelperOutput2);
            });

            resetEvent1.Set();
            await Task.WhenAll(task1, task2);

            // Assert
            Assert.Empty(tagHelperOutput1.PreContent.GetContent());
            Assert.Empty(tagHelperOutput1.PostContent.GetContent());
            Assert.False(tagHelperOutput1.IsContentModified);
            Assert.Empty(tagHelperOutput1.Content.GetContent());

            Assert.Empty(tagHelperOutput2.PreContent.GetContent());
            Assert.Empty(tagHelperOutput2.PostContent.GetContent());
            Assert.True(tagHelperOutput2.IsContentModified);
            Assert.Equal(childContent, tagHelperOutput2.Content.GetContent());

            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task Deserialize_DoesntAlterValue_WhenSerialized()
        {
            // Arrange
            var content = "<b>some content</b>";
            var formatter = GetFormatter();
            var context = new DistributedCacheTagHelperFormattingContext
            {
                Html = new HtmlString(content)
            };
            var serialized = await formatter.SerializeAsync(context);

            // Act
            var deserialized = await formatter.DeserializeAsync(serialized);
            
            // Assert
            Assert.Equal(deserialized.ToString(), content);
        }

        private static ViewContext GetViewContext()
        {
            var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
            return new ViewContext(actionContext,
                Mock.Of<IView>(),
                new ViewDataDictionary(new EmptyModelMetadataProvider()),
                Mock.Of<ITempDataDictionary>(),
                TextWriter.Null,
                new HtmlHelperOptions());
        }

        private static TagHelperContext GetTagHelperContext()
        {
            return new TagHelperContext(
                allAttributes: new TagHelperAttributeList(),
                items: new Dictionary<object, object>(),
                uniqueId: "testid");
        }

        private static TagHelperOutput GetTagHelperOutput(
            string tagName = "distributed-cache",
            TagHelperAttributeList attributes = null,
            string childContent = "some child content")
        {
            attributes = attributes ?? new TagHelperAttributeList { { "attr", "value" } };

            return new TagHelperOutput(
                tagName,
                attributes,
                getChildContentAsync: (useCachedResult, encoder) =>
                {
                    var tagHelperContent = new DefaultTagHelperContent();
                    tagHelperContent.SetHtmlContent(childContent);
                    return Task.FromResult<TagHelperContent>(tagHelperContent);
                });
        }

        private static string GetHashedBytes(string input)
        {
            using (var sha = SHA256.Create())
            {
                var contentBytes = Encoding.UTF8.GetBytes(input);
                var hashedBytes = sha.ComputeHash(contentBytes);
                return Convert.ToBase64String(hashedBytes);
            }
        }

        private static IDistributedCacheTagHelperStorage GetStorage(MemoryCacheOptions options = null)
        {
            return new DistributedCacheTagHelperStorage(new MemoryDistributedCache(new MemoryCache(options ?? new MemoryCacheOptions())));
        }
        
        private static IDistributedCacheTagHelperFormatter GetFormatter()
        {
            return new DistributedCacheTagHelperFormatter();
        }
    }
}
