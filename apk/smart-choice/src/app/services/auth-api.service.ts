import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthTokenResponse, GuestTokenResponse } from '../models/api.models';

const SkipAuthHeader = 'x-skip-auth';

@Injectable({ providedIn: 'root' })
export class AuthApiService {
  constructor(private readonly httpClient: HttpClient) {}

  login(email: string, password: string): Observable<AuthTokenResponse> {
    return this.httpClient.post<AuthTokenResponse>(
      `${environment.apiBaseUrl}/api/auth/login`,
      { email, password },
      { headers: new HttpHeaders({ [SkipAuthHeader]: '1' }) }
    );
  }

  issueGuestToken(inviteCode: string): Observable<GuestTokenResponse> {
    return this.httpClient.post<GuestTokenResponse>(
      `${environment.apiBaseUrl}/api/auth/guest`,
      { inviteCode },
      { headers: new HttpHeaders({ [SkipAuthHeader]: '1' }) }
    );
  }
}
