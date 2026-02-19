using Cortex.Mediator.Commands;
using Harmony.Application.Contract.Responses;

namespace Harmony.Application.Contract.Requests;

public record AddPostDto() : ICommand<PostDto>;
