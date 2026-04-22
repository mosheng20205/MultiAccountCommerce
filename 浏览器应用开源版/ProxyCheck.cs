using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed class ProxyCheckRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "HTTP";
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string User { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string TargetUrl { get; set; } = string.Empty;
        public int Rounds { get; set; } = 3;
        public int TimeoutMs { get; set; } = 5000;
    }

    internal sealed class ProxyCheckResponse
    {
        public bool Success { get; set; }
        public string Status { get; set; } = string.Empty;
        public string SummaryMessage { get; set; } = string.Empty;
        public int LatencyMs { get; set; }
        public string ExitIp { get; set; } = string.Empty;
        public string ExitRegion { get; set; } = string.Empty;
        public string TargetUrl { get; set; } = string.Empty;
        public int SuccessRounds { get; set; }
        public int TotalRounds { get; set; }
        public int Score { get; set; }
        public string Grade { get; set; } = string.Empty;
    }

    internal sealed class ProxyCheckRoundResult
    {
        public bool Success { get; set; }
        public bool Risky { get; set; }
        public int LatencyMs { get; set; }
        public string ExitIp { get; set; } = string.Empty;
        public string ExitRegion { get; set; } = string.Empty;
        public string TargetUrl { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public ProxyCheckStageResult LowLevel { get; set; } = new ProxyCheckStageResult();
        public ProxyCheckStageResult OutboundIdentity { get; set; } = new ProxyCheckStageResult();
        public ProxyCheckStageResult TargetSite { get; set; } = new ProxyCheckStageResult();
    }

    internal sealed class ProxyCheckStageResult
    {
        public bool Success { get; set; }
        public bool Risky { get; set; }
        public int LatencyMs { get; set; }
        public int StatusCode { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ExitIp { get; set; } = string.Empty;
        public string ExitRegion { get; set; } = string.Empty;
        public string TargetUrl { get; set; } = string.Empty;
    }

    internal static class ProxyCheck
    {
        private static readonly string[] RiskKeywords =
        {
            "captcha",
            "verify",
            "verification",
            "access denied",
            "forbidden",
            "unusual traffic",
            "blocked"
        };

        private static readonly Uri[] OutboundIpEndpoints =
        {
            new Uri("http://api.ipify.org/?format=json"),
            new Uri("http://ifconfig.me/ip"),
            new Uri("http://ip-api.com/json/?fields=query,country,regionName,city")
        };

        private static readonly Uri DefaultTargetUrl = new Uri("https://example.com/");

        public static ProxyCheckResponse RunProxyCheck(ProxyCheckRequest request)
        {
            if (request == null)
            {
                return BuildFailure("不可用", "代理配置为空。");
            }

            if (string.IsNullOrWhiteSpace(request.Host) || request.Port <= 0 || request.Port > 65535)
            {
                return BuildFailure("不可用", "代理主机或端口无效。");
            }

            if (string.Equals(request.Type, "SOCKS5", StringComparison.OrdinalIgnoreCase)
                && (!string.IsNullOrWhiteSpace(request.User) || !string.IsNullOrWhiteSpace(request.Password)))
            {
                return BuildFailure("不可用", "开源版暂不支持带账号密码的 SOCKS5 代理。");
            }

            List<ProxyCheckRoundResult> rounds = new List<ProxyCheckRoundResult>();
            int totalRounds = Math.Max(1, request.Rounds);
            for (int i = 0; i < totalRounds; i++)
            {
                rounds.Add(RunSingleRound(request));
            }

            return AggregateRounds(request, rounds);
        }

        private static ProxyCheckRoundResult RunSingleRound(ProxyCheckRequest request)
        {
            ProxyCheckRoundResult round = new ProxyCheckRoundResult();
            Stopwatch watch = Stopwatch.StartNew();

            round.LowLevel = RunLowLevelCheck(request);
            if (!round.LowLevel.Success)
            {
                watch.Stop();
                round.LatencyMs = round.LowLevel.LatencyMs > 0 ? round.LowLevel.LatencyMs : (int)watch.ElapsedMilliseconds;
                round.Summary = round.LowLevel.Message;
                return round;
            }

            round.OutboundIdentity = RunOutboundIdentityCheck(request);
            if (!round.OutboundIdentity.Success)
            {
                watch.Stop();
                round.LatencyMs = Math.Max(round.LowLevel.LatencyMs + round.OutboundIdentity.LatencyMs, (int)watch.ElapsedMilliseconds);
                round.Summary = round.OutboundIdentity.Message;
                return round;
            }

            round.ExitIp = round.OutboundIdentity.ExitIp;
            round.ExitRegion = round.OutboundIdentity.ExitRegion;
            round.TargetSite = RunTargetSiteCheck(request);
            round.TargetUrl = round.TargetSite.TargetUrl;
            round.Risky = round.TargetSite.Risky;
            round.Success = round.OutboundIdentity.Success && round.TargetSite.Success;

            watch.Stop();
            round.LatencyMs = (int)watch.ElapsedMilliseconds;
            if (round.Success)
            {
                round.Summary = $"出口IP {round.ExitIp} | {FormatRegion(round.ExitRegion)} | 目标站通过";
            }
            else if (round.Risky)
            {
                round.Summary = $"出口IP {round.ExitIp} | {FormatRegion(round.ExitRegion)} | 目标站风险";
            }
            else
            {
                round.Summary = round.TargetSite.Message;
            }

            return round;
        }

        private static ProxyCheckStageResult RunLowLevelCheck(ProxyCheckRequest request)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                using (TcpClient client = ConnectProxy(request))
                using (Stream stream = client.GetStream())
                {
                    if (IsSocks5(request))
                    {
                        PerformSocks5Handshake(stream, request, "1.1.1.1", 80);
                        watch.Stop();
                        return new ProxyCheckStageResult
                        {
                            Success = true,
                            LatencyMs = (int)watch.ElapsedMilliseconds,
                            Status = "通过",
                            Message = $"SOCKS5 握手成功，延迟 {watch.ElapsedMilliseconds}ms。"
                        };
                    }

                    SendHttpConnect(stream, request, "1.1.1.1", 80);
                    SimpleHttpResponse response = ReadHttpResponse(stream, request.TimeoutMs);
                    watch.Stop();

                    if (response.StatusCode == 200)
                    {
                        return new ProxyCheckStageResult
                        {
                            Success = true,
                            LatencyMs = (int)watch.ElapsedMilliseconds,
                            Status = "通过",
                            Message = $"HTTP CONNECT 成功，延迟 {watch.ElapsedMilliseconds}ms。"
                        };
                    }

                    if (response.StatusCode == 407)
                    {
                        return new ProxyCheckStageResult
                        {
                            Success = false,
                            LatencyMs = (int)watch.ElapsedMilliseconds,
                            Status = "失败",
                            Message = "代理认证失败，请检查用户名或密码。"
                        };
                    }

                    return new ProxyCheckStageResult
                    {
                        Success = false,
                        LatencyMs = (int)watch.ElapsedMilliseconds,
                        Status = "失败",
                        Message = $"代理握手失败：HTTP {response.StatusCode} {response.ReasonPhrase}".Trim()
                    };
                }
            }
            catch (Exception ex)
            {
                watch.Stop();
                return new ProxyCheckStageResult
                {
                    Success = false,
                    LatencyMs = (int)watch.ElapsedMilliseconds,
                    Status = "失败",
                    Message = $"低层连通失败：{ex.Message}"
                };
            }
        }

        private static ProxyCheckStageResult RunOutboundIdentityCheck(ProxyCheckRequest request)
        {
            Stopwatch watch = Stopwatch.StartNew();
            List<ProxyCheckStageResult> successes = new List<ProxyCheckStageResult>();

            foreach (Uri endpoint in OutboundIpEndpoints)
            {
                ProxyCheckStageResult probe = ProbeOutboundIdentity(request, endpoint);
                if (probe.Success && !string.IsNullOrWhiteSpace(probe.ExitIp))
                {
                    successes.Add(probe);
                }
            }

            watch.Stop();
            if (successes.Count < 2)
            {
                return new ProxyCheckStageResult
                {
                    Success = false,
                    LatencyMs = (int)watch.ElapsedMilliseconds,
                    Status = "失败",
                    Message = "出口 IP 校验不足，成功接口少于 2 个。"
                };
            }

            IGrouping<string, ProxyCheckStageResult> consensus = successes
                .GroupBy(item => item.ExitIp, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .First();

            if (consensus.Count() < 2)
            {
                return new ProxyCheckStageResult
                {
                    Success = false,
                    LatencyMs = (int)watch.ElapsedMilliseconds,
                    Status = "失败",
                    Message = "多个出口 IP 接口返回不一致，代理出口不稳定。"
                };
            }

            ProxyCheckStageResult regionSource = consensus.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.ExitRegion))
                ?? consensus.First();

            return new ProxyCheckStageResult
            {
                Success = true,
                LatencyMs = (int)watch.ElapsedMilliseconds,
                Status = "通过",
                Message = $"出口 IP 校验通过：{consensus.Key}",
                ExitIp = consensus.Key,
                ExitRegion = regionSource.ExitRegion
            };
        }

        private static ProxyCheckStageResult ProbeOutboundIdentity(ProxyCheckRequest request, Uri endpoint)
        {
            try
            {
                SimpleHttpResponse response = ExecuteGetRequest(request, endpoint);
                if (response.StatusCode < 200 || response.StatusCode >= 400)
                {
                    return new ProxyCheckStageResult
                    {
                        Success = false,
                        StatusCode = response.StatusCode,
                        Status = "失败",
                        Message = $"IP 接口响应异常：HTTP {response.StatusCode}"
                    };
                }

                string ip = ExtractIpAddress(response.Body);
                string region = ExtractRegion(response.Body);
                return new ProxyCheckStageResult
                {
                    Success = !string.IsNullOrWhiteSpace(ip),
                    StatusCode = response.StatusCode,
                    Status = !string.IsNullOrWhiteSpace(ip) ? "通过" : "失败",
                    Message = !string.IsNullOrWhiteSpace(ip) ? $"出口 IP：{ip}" : "IP 接口未返回有效公网 IP。",
                    ExitIp = ip,
                    ExitRegion = region
                };
            }
            catch (Exception ex)
            {
                return new ProxyCheckStageResult
                {
                    Success = false,
                    Status = "失败",
                    Message = $"IP 接口探测失败：{ex.Message}"
                };
            }
        }

        private static ProxyCheckStageResult RunTargetSiteCheck(ProxyCheckRequest request)
        {
            Uri targetUri = ResolveTargetUri(request.TargetUrl);
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                SimpleHttpResponse response = ExecuteGetRequest(request, targetUri);
                watch.Stop();

                string haystack = $"{response.HeadersText}\n{response.Body}".ToLowerInvariant();
                bool risky = RiskKeywords.Any(keyword => haystack.Contains(keyword));
                bool statusAllowed = response.StatusCode == 200
                    || response.StatusCode == 301
                    || response.StatusCode == 302
                    || response.StatusCode == 401
                    || response.StatusCode == 403;

                if (statusAllowed && !risky)
                {
                    return new ProxyCheckStageResult
                    {
                        Success = true,
                        LatencyMs = (int)watch.ElapsedMilliseconds,
                        StatusCode = response.StatusCode,
                        Status = "通过",
                        Message = $"目标站响应正常：HTTP {response.StatusCode}",
                        TargetUrl = targetUri.ToString()
                    };
                }

                if (statusAllowed && risky)
                {
                    return new ProxyCheckStageResult
                    {
                        Success = false,
                        Risky = true,
                        LatencyMs = (int)watch.ElapsedMilliseconds,
                        StatusCode = response.StatusCode,
                        Status = "风险",
                        Message = $"目标站疑似风控：HTTP {response.StatusCode}",
                        TargetUrl = targetUri.ToString()
                    };
                }

                return new ProxyCheckStageResult
                {
                    Success = false,
                    LatencyMs = (int)watch.ElapsedMilliseconds,
                    StatusCode = response.StatusCode,
                    Status = "失败",
                    Message = $"目标站访问失败：HTTP {response.StatusCode}",
                    TargetUrl = targetUri.ToString()
                };
            }
            catch (Exception ex)
            {
                watch.Stop();
                return new ProxyCheckStageResult
                {
                    Success = false,
                    LatencyMs = (int)watch.ElapsedMilliseconds,
                    Status = "失败",
                    Message = $"目标站访问失败：{ex.Message}",
                    TargetUrl = targetUri.ToString()
                };
            }
        }

        private static ProxyCheckResponse AggregateRounds(ProxyCheckRequest request, List<ProxyCheckRoundResult> rounds)
        {
            int totalRounds = rounds.Count;
            int successRounds = rounds.Count(item => item.Success);
            bool hasRisk = rounds.Any(item => item.Risky);
            string exitIp = rounds.Select(item => item.ExitIp).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            string exitRegion = rounds.Select(item => item.ExitRegion).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
            string targetUrl = rounds.Select(item => item.TargetUrl).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? ResolveTargetUri(request.TargetUrl).ToString();

            List<int> latencyPool = rounds.Where(item => item.LatencyMs > 0).Select(item => item.LatencyMs).ToList();
            int avgLatency = latencyPool.Count > 0 ? (int)Math.Round(latencyPool.Average()) : 0;

            string grade;
            int score;
            if (successRounds == totalRounds && totalRounds > 0)
            {
                if (avgLatency > 0 && avgLatency <= 800)
                {
                    grade = "优秀";
                    score = 95;
                }
                else
                {
                    grade = "可用";
                    score = 82;
                }
            }
            else if (successRounds >= Math.Max(1, totalRounds - 1))
            {
                grade = "可用";
                score = 76;
            }
            else if (hasRisk || successRounds > 0)
            {
                grade = "风险";
                score = 52;
            }
            else
            {
                grade = "不可用";
                score = 18;
            }

            string targetStatus = successRounds > 0 ? "目标站通过" : (hasRisk ? "目标站风险" : "目标站失败");
            string summary = $"出口IP {Fallback(exitIp, "未知")} | {FormatRegion(exitRegion)} | {targetStatus} | {successRounds}/{totalRounds} | 平均 {avgLatency}ms";

            return new ProxyCheckResponse
            {
                Success = grade == "优秀" || grade == "可用",
                Status = grade,
                SummaryMessage = summary,
                LatencyMs = avgLatency,
                ExitIp = exitIp,
                ExitRegion = exitRegion,
                TargetUrl = targetUrl,
                SuccessRounds = successRounds,
                TotalRounds = totalRounds,
                Score = score,
                Grade = grade
            };
        }

        private static ProxyCheckResponse BuildFailure(string status, string message)
        {
            return new ProxyCheckResponse
            {
                Success = false,
                Status = status,
                SummaryMessage = message,
                Grade = status,
                TotalRounds = 1
            };
        }

        private static SimpleHttpResponse ExecuteGetRequest(ProxyCheckRequest request, Uri targetUri)
        {
            using (TcpClient client = ConnectProxy(request))
            using (Stream tunnel = CreateRequestTunnel(client, request, targetUri))
            {
                string requestText = BuildGetRequestText(request, targetUri);
                byte[] payload = Encoding.ASCII.GetBytes(requestText);
                tunnel.Write(payload, 0, payload.Length);
                tunnel.Flush();
                return ReadHttpResponse(tunnel, request.TimeoutMs);
            }
        }

        private static Stream CreateRequestTunnel(TcpClient client, ProxyCheckRequest request, Uri targetUri)
        {
            Stream stream = client.GetStream();
            if (IsSocks5(request))
            {
                PerformSocks5Handshake(stream, request, targetUri.Host, targetUri.Port);
                return WrapTlsIfNeeded(stream, targetUri, request.TimeoutMs);
            }

            if (string.Equals(targetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                SendHttpConnect(stream, request, targetUri.Host, targetUri.Port);
                SimpleHttpResponse connectResponse = ReadHttpResponse(stream, request.TimeoutMs);
                if (connectResponse.StatusCode != 200)
                {
                    throw new IOException($"HTTP CONNECT 失败：{connectResponse.StatusCode} {connectResponse.ReasonPhrase}".Trim());
                }

                return WrapTlsIfNeeded(stream, targetUri, request.TimeoutMs);
            }

            return stream;
        }

        private static TcpClient ConnectProxy(ProxyCheckRequest request)
        {
            TcpClient client = new TcpClient();
            if (!client.ConnectAsync(request.Host, request.Port).Wait(request.TimeoutMs))
            {
                client.Dispose();
                throw new TimeoutException("连接代理服务器超时。");
            }

            client.ReceiveTimeout = request.TimeoutMs;
            client.SendTimeout = request.TimeoutMs;
            return client;
        }

        private static Stream WrapTlsIfNeeded(Stream stream, Uri targetUri, int timeoutMs)
        {
            if (!string.Equals(targetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return stream;
            }

            SslStream ssl = new SslStream(stream, false, IgnoreCertificateErrors);
            ssl.ReadTimeout = timeoutMs;
            ssl.WriteTimeout = timeoutMs;
            ssl.AuthenticateAsClient(targetUri.Host);
            return ssl;
        }

        private static bool IgnoreCertificateErrors(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        private static void SendHttpConnect(Stream stream, ProxyCheckRequest request, string host, int port)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendFormat(CultureInfo.InvariantCulture, "CONNECT {0}:{1} HTTP/1.1\r\n", host, port);
            builder.AppendFormat(CultureInfo.InvariantCulture, "Host: {0}:{1}\r\n", host, port);
            builder.Append("Proxy-Connection: Keep-Alive\r\n");
            builder.Append("User-Agent: ProxyCheck/1.0\r\n");
            AppendProxyAuthorization(builder, request);
            builder.Append("\r\n");

            byte[] bytes = Encoding.ASCII.GetBytes(builder.ToString());
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string BuildGetRequestText(ProxyCheckRequest request, Uri targetUri)
        {
            bool plainHttpThroughHttpProxy = !IsSocks5(request)
                && string.Equals(targetUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

            string requestTarget = plainHttpThroughHttpProxy ? targetUri.AbsoluteUri : targetUri.PathAndQuery;
            if (string.IsNullOrWhiteSpace(requestTarget))
            {
                requestTarget = "/";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendFormat(CultureInfo.InvariantCulture, "GET {0} HTTP/1.1\r\n", requestTarget);
            builder.AppendFormat(CultureInfo.InvariantCulture, "Host: {0}\r\n", targetUri.IsDefaultPort ? targetUri.Host : $"{targetUri.Host}:{targetUri.Port}");
            builder.Append("Connection: close\r\n");
            builder.Append("Accept: */*\r\n");
            builder.Append("Accept-Encoding: identity\r\n");
            builder.Append("User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) ProxyCheck/1.0\r\n");
            if (plainHttpThroughHttpProxy)
            {
                AppendProxyAuthorization(builder, request);
            }

            builder.Append("\r\n");
            return builder.ToString();
        }

        private static void AppendProxyAuthorization(StringBuilder builder, ProxyCheckRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.User) && string.IsNullOrWhiteSpace(request.Password))
            {
                return;
            }

            string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{request.User}:{request.Password}"));
            builder.AppendFormat(CultureInfo.InvariantCulture, "Proxy-Authorization: Basic {0}\r\n", auth);
        }

        private static void PerformSocks5Handshake(Stream stream, ProxyCheckRequest request, string destinationHost, int destinationPort)
        {
            byte[] hello = { 0x05, 0x01, 0x00 };
            stream.Write(hello, 0, hello.Length);
            stream.Flush();

            byte[] helloResponse = ReadExact(stream, 2);
            if (helloResponse[0] != 0x05 || helloResponse[1] != 0x00)
            {
                throw new IOException("SOCKS5 握手失败或代理要求认证。");
            }

            List<byte> connect = new List<byte> { 0x05, 0x01, 0x00 };
            if (IPAddressRegex.IsMatch(destinationHost))
            {
                connect.Add(0x01);
                connect.AddRange(ParseIpv4(destinationHost));
            }
            else
            {
                byte[] hostBytes = Encoding.ASCII.GetBytes(destinationHost);
                if (hostBytes.Length > 255)
                {
                    throw new IOException("SOCKS5 目标主机名过长。");
                }

                connect.Add(0x03);
                connect.Add((byte)hostBytes.Length);
                connect.AddRange(hostBytes);
            }

            connect.Add((byte)((destinationPort >> 8) & 0xFF));
            connect.Add((byte)(destinationPort & 0xFF));

            byte[] payload = connect.ToArray();
            stream.Write(payload, 0, payload.Length);
            stream.Flush();

            byte[] head = ReadExact(stream, 4);
            if (head[1] != 0x00)
            {
                throw new IOException($"SOCKS5 CONNECT 被拒绝，错误码 {head[1]}。");
            }

            int addressLength = 0;
            switch (head[3])
            {
                case 0x01:
                    addressLength = 4;
                    break;
                case 0x04:
                    addressLength = 16;
                    break;
                case 0x03:
                    addressLength = ReadExact(stream, 1)[0];
                    break;
            }

            if (addressLength > 0)
            {
                ReadExact(stream, addressLength + 2);
            }
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new IOException("代理连接被远端中断。");
                }

                offset += read;
            }

            return buffer;
        }

        private static SimpleHttpResponse ReadHttpResponse(Stream stream, int timeoutMs)
        {
            MemoryStream buffer = new MemoryStream();
            byte[] chunk = new byte[4096];
            int headerEndIndex = -1;
            int contentLength = -1;
            bool chunked = false;

            while (true)
            {
                int read;
                try
                {
                    read = stream.Read(chunk, 0, chunk.Length);
                }
                catch (IOException)
                {
                    if (buffer.Length == 0)
                    {
                        throw;
                    }

                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                buffer.Write(chunk, 0, read);
                byte[] current = buffer.ToArray();
                if (headerEndIndex < 0)
                {
                    headerEndIndex = FindHeaderEnd(current);
                    if (headerEndIndex >= 0)
                    {
                        string headerText = Encoding.ASCII.GetString(current, 0, headerEndIndex);
                        contentLength = ParseContentLength(headerText);
                        chunked = headerText.IndexOf("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }

                if (headerEndIndex >= 0)
                {
                    int bodyStart = headerEndIndex + 4;
                    if (contentLength >= 0 && current.Length >= bodyStart + contentLength)
                    {
                        break;
                    }

                    if (chunked && EndsWithChunkTerminator(current, bodyStart))
                    {
                        break;
                    }
                }

                if (buffer.Length > 1024 * 128)
                {
                    break;
                }
            }

            byte[] bytes = buffer.ToArray();
            int separator = FindHeaderEnd(bytes);
            string headerSection = separator >= 0 ? Encoding.ASCII.GetString(bytes, 0, separator) : Encoding.ASCII.GetString(bytes);
            string body = separator >= 0 && separator + 4 <= bytes.Length
                ? Encoding.UTF8.GetString(bytes, separator + 4, bytes.Length - separator - 4)
                : string.Empty;

            string[] headerLines = headerSection.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string statusLine = headerLines.Length > 0 ? headerLines[0] : string.Empty;
            int statusCode = 0;
            string reasonPhrase = string.Empty;
            string[] parts = statusLine.Split(' ');
            if (parts.Length >= 2)
            {
                int.TryParse(parts[1], out statusCode);
                reasonPhrase = string.Join(" ", parts.Skip(2));
            }

            return new SimpleHttpResponse
            {
                StatusCode = statusCode,
                ReasonPhrase = reasonPhrase,
                HeadersText = headerSection,
                Body = body
            };
        }

        private static int FindHeaderEnd(byte[] bytes)
        {
            for (int i = 0; i <= bytes.Length - 4; i++)
            {
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int ParseContentLength(string headerText)
        {
            Match match = Regex.Match(headerText, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return -1;
            }

            return int.TryParse(match.Groups[1].Value, out int length) ? length : -1;
        }

        private static bool EndsWithChunkTerminator(byte[] bytes, int bodyStart)
        {
            if (bytes.Length <= bodyStart)
            {
                return false;
            }

            int start = Math.Max(bodyStart, bytes.Length - 16);
            string bodyTail = Encoding.ASCII.GetString(bytes, start, bytes.Length - start);
            return bodyTail.Contains("0\r\n\r\n");
        }

        private static Uri ResolveTargetUri(string targetUrl)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                return DefaultTargetUrl;
            }

            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out Uri uri))
            {
                if (!Uri.TryCreate("https://" + targetUrl.Trim(), UriKind.Absolute, out uri))
                {
                    return DefaultTargetUrl;
                }
            }

            return uri;
        }

        private static bool IsSocks5(ProxyCheckRequest request)
        {
            return string.Equals(request.Type, "SOCKS5", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractIpAddress(string text)
        {
            Match match = IPAddressRegex.Match(text ?? string.Empty);
            return match.Success ? match.Value : string.Empty;
        }

        private static string ExtractRegion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string country = ExtractJsonValue(text, "country");
            string region = ExtractJsonValue(text, "regionName");
            string city = ExtractJsonValue(text, "city");
            return string.Join("/", new[] { country, region, city }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string ExtractJsonValue(string text, string key)
        {
            Match match = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private static byte[] ParseIpv4(string ip)
        {
            string[] parts = ip.Split('.');
            if (parts.Length != 4)
            {
                throw new IOException("IPv4 地址格式无效。");
            }

            return parts.Select(part => byte.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        }

        private static string Fallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string FormatRegion(string region)
        {
            return string.IsNullOrWhiteSpace(region) ? "地区未知" : region;
        }

        private static readonly Regex IPAddressRegex = new Regex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);

        private sealed class SimpleHttpResponse
        {
            public int StatusCode { get; set; }
            public string ReasonPhrase { get; set; } = string.Empty;
            public string HeadersText { get; set; } = string.Empty;
            public string Body { get; set; } = string.Empty;
        }
    }
}
