using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.SharedKernel.Messaging;

public interface IQuery<out TResponse> : IRequest<TResponse>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;