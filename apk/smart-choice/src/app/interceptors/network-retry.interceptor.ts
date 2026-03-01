import { Injectable } from '@angular/core';
import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { Observable, retry, throwError, timer } from 'rxjs';

@Injectable()
export class NetworkRetryInterceptor implements HttpInterceptor {
  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (!isRetryableMethod(request.method)) {
      return next.handle(request);
    }

    return next.handle(request).pipe(
      retry({
        count: 2,
        delay: (error: unknown, retryCount: number) => {
          if (!isRetryableError(error)) {
            return throwError(() => error);
          }

          const delayMs = Math.min(400 * retryCount, 1_200);
          return timer(delayMs);
        },
      })
    );
  }
}

function isRetryableMethod(method: string): boolean {
  return method === 'GET' || method === 'HEAD';
}

function isRetryableError(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) {
    return false;
  }

  return error.status === 0 || error.status >= 500 || error.status === 429;
}
