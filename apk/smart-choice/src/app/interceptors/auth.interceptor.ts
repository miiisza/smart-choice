import { Injectable } from '@angular/core';
import {
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthSessionService } from '../services/auth-session.service';

const SkipAuthHeader = 'x-skip-auth';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private readonly authSessionService: AuthSessionService) {}

  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    const requestWithoutSkipHeader = request.headers.has(SkipAuthHeader)
      ? request.clone({ headers: request.headers.delete(SkipAuthHeader) })
      : request;

    if (request.headers.has(SkipAuthHeader) || !isApiRequest(request.url)) {
      return next.handle(requestWithoutSkipHeader);
    }

    if (isAuthEndpoint(request.url)) {
      return next.handle(requestWithoutSkipHeader);
    }

    const accessToken = this.authSessionService.getAccessToken();
    if (!accessToken || request.headers.has('Authorization')) {
      return next.handle(requestWithoutSkipHeader);
    }

    const authorizedRequest = requestWithoutSkipHeader.clone({
      setHeaders: {
        Authorization: `Bearer ${accessToken}`,
      },
    });

    return next.handle(authorizedRequest);
  }
}

function isApiRequest(url: string): boolean {
  return url.startsWith(environment.apiBaseUrl);
}

function isAuthEndpoint(url: string): boolean {
  return url.startsWith(`${environment.apiBaseUrl}/api/auth/`);
}
