using Cortex.Mediator.Commands;
using Harmony.Application.Contract.Requests;
using Harmony.Application.Contract.Responses;

public class AddPostHandler : ICommandHandler<AddPostDto, PostDto>
{
    public async Task<PostDto> Handle(AddPostDto command, CancellationToken cancellationToken = default)
    {
        return new PostDto
        {
        };
    }
}