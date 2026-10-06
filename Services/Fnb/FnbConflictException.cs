using System;
namespace SnowmeetApi.Services.Fnb;
public sealed class FnbConflictException(string message) : Exception(message);
