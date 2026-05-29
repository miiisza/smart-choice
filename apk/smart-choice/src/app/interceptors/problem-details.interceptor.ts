import { Injectable } from '@angular/core';
import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { Observable, catchError, throwError } from 'rxjs';
import { ProblemDetails } from '../models/api.models';

export class ApiProblemError extends Error {
  constructor(
    public readonly problem: ProblemDetails,
    public readonly statusCode: number
  ) {
    super(buildProblemMessage(problem, statusCode));
    this.name = 'ApiProblemError';
  }
}

@Injectable()
export class ProblemDetailsInterceptor implements HttpInterceptor {
  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    return next.handle(request).pipe(
      catchError((error: unknown) => {
        if (error instanceof ApiProblemError) {
          return throwError(() => error);
        }

        if (error instanceof HttpErrorResponse) {
          const problem = normalizeProblemDetails(error);
          if (problem) {
            return throwError(() => new ApiProblemError(problem, error.status));
          }
        }

        return throwError(() => error);
      })
    );
  }
}

function normalizeProblemDetails(error: HttpErrorResponse): ProblemDetails | null {
  const payload = error.error;
  if (!payload || typeof payload !== 'object') {
    return null;
  }

  const asProblem = payload as ProblemDetails;
  const hasProblemSignature =
    typeof asProblem.title === 'string' ||
    typeof asProblem.detail === 'string' ||
    typeof asProblem.status === 'number' ||
    typeof asProblem.errors === 'object';

  if (!hasProblemSignature) {
    return null;
  }

  return {
    type: asProblem.type,
    title: asProblem.title,
    status: asProblem.status,
    detail: asProblem.detail,
    instance: asProblem.instance,
    errors: asProblem.errors,
  };
}

function buildProblemMessage(problem: ProblemDetails, statusCode: number): string {
  if (problem.detail && problem.detail.trim().length > 0) {
    return problem.detail;
  }

  const validationMessages: string[] = Object.values(problem.errors ?? {}).reduce(
    (allMessages: string[], fieldMessages: string[]) => allMessages.concat(fieldMessages),
    []
  );
  const firstValidationMessage = validationMessages.find(
    (message: string) => message.trim().length > 0
  );

  if (firstValidationMessage) {
    return firstValidationMessage;
  }

  if (problem.title && problem.title.trim().length > 0) {
    return problem.title;
  }

  if (statusCode > 0) {
    return `Request failed (HTTP ${statusCode}).`;
  }

  return 'Request failed.';
}
