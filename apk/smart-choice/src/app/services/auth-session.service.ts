import { Injectable } from '@angular/core';
import { AuthTokenResponse, GuestTokenResponse } from '../models/api.models';

export type SessionActor = 'user' | 'guest';

interface StoredSession {
  actor: SessionActor;
  accessToken: string;
  tokenType: string;
  accessTokenExpiresAt: string;
  pollId?: number;
  refreshToken?: string;
  refreshTokenExpiresAt?: string;
}

@Injectable({ providedIn: 'root' })
export class AuthSessionService {
  private readonly storageKey = 'smartchoice.session.v1';
  private session: StoredSession | null = this.loadSession();

  getAccessToken(): string | null {
    return this.session?.accessToken ?? null;
  }

  getActor(): SessionActor | null {
    return this.session?.actor ?? null;
  }

  hasToken(): boolean {
    return Boolean(this.session?.accessToken);
  }

  canVoteOnPoll(pollId: number): boolean {
    if (!this.session?.accessToken) {
      return false;
    }

    if (this.session.actor === 'user') {
      return true;
    }

    return this.session.pollId === pollId;
  }

  isUserSession(): boolean {
    return this.session?.actor === 'user';
  }

  setUserSession(tokens: AuthTokenResponse): void {
    this.session = {
      actor: 'user',
      accessToken: tokens.accessToken,
      tokenType: tokens.tokenType,
      accessTokenExpiresAt: tokens.accessTokenExpiresAt,
      refreshToken: tokens.refreshToken,
      refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
    };

    this.persistSession();
  }

  setGuestSession(tokens: GuestTokenResponse): void {
    this.session = {
      actor: 'guest',
      accessToken: tokens.guestToken,
      tokenType: tokens.tokenType,
      accessTokenExpiresAt: tokens.expiresAt,
      pollId: tokens.pollId,
    };

    this.persistSession();
  }

  clear(): void {
    this.session = null;
    localStorage.removeItem(this.storageKey);
  }

  private loadSession(): StoredSession | null {
    const rawSession = localStorage.getItem(this.storageKey);
    if (!rawSession) {
      return null;
    }

    try {
      const parsed = JSON.parse(rawSession) as Partial<StoredSession>;
      if (
        (parsed.actor !== 'user' && parsed.actor !== 'guest') ||
        typeof parsed.accessToken !== 'string' ||
        parsed.accessToken.trim().length === 0 ||
        typeof parsed.tokenType !== 'string' ||
        parsed.tokenType.trim().length === 0 ||
        typeof parsed.accessTokenExpiresAt !== 'string'
      ) {
        return null;
      }

      if (
        parsed.actor === 'guest' &&
        (typeof parsed.pollId !== 'number' || !Number.isInteger(parsed.pollId) || parsed.pollId <= 0)
      ) {
        return null;
      }

      return {
        actor: parsed.actor,
        accessToken: parsed.accessToken,
        tokenType: parsed.tokenType,
        accessTokenExpiresAt: parsed.accessTokenExpiresAt,
        pollId: typeof parsed.pollId === 'number' ? parsed.pollId : undefined,
        refreshToken: parsed.refreshToken,
        refreshTokenExpiresAt: parsed.refreshTokenExpiresAt,
      };
    } catch {
      return null;
    }
  }

  private persistSession(): void {
    if (!this.session) {
      return;
    }

    localStorage.setItem(this.storageKey, JSON.stringify(this.session));
  }
}
