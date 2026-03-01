import { Component, NgZone, OnDestroy, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { Capacitor, PluginListenerHandle, registerPlugin } from '@capacitor/core';

interface AppUrlOpenEvent {
  url: string;
}

interface LaunchUrlResult {
  url?: string;
}

interface CapacitorAppPlugin {
  getLaunchUrl(): Promise<LaunchUrlResult>;
  addListener(
    eventName: 'appUrlOpen',
    listenerFunc: (event: AppUrlOpenEvent) => void
  ): Promise<PluginListenerHandle>;
}

const CapacitorApp = registerPlugin<CapacitorAppPlugin>('App');

@Component({
  selector: 'app-root',
  templateUrl: 'app.component.html',
  styleUrls: ['app.component.scss'],
  standalone: false,
})
export class AppComponent implements OnInit, OnDestroy {
  private appUrlListener?: PluginListenerHandle;

  constructor(private readonly router: Router, private readonly ngZone: NgZone) {}

  async ngOnInit(): Promise<void> {
    if (!Capacitor.isNativePlatform()) {
      return;
    }

    await this.handleLaunchUrl();
    await this.handleRuntimeDeepLinks();
  }

  ngOnDestroy(): void {
    void this.appUrlListener?.remove();
  }

  private async handleLaunchUrl(): Promise<void> {
    try {
      const launchUrl = await CapacitorApp.getLaunchUrl();
      this.navigateFromIncomingUrl(launchUrl.url);
    } catch {
      // App plugin may be unavailable in browser builds.
    }
  }

  private async handleRuntimeDeepLinks(): Promise<void> {
    try {
      this.appUrlListener = await CapacitorApp.addListener('appUrlOpen', ({ url }) => {
        this.ngZone.run(() => {
          this.navigateFromIncomingUrl(url);
        });
      });
    } catch {
      // App plugin may be unavailable in browser builds.
    }
  }

  private navigateFromIncomingUrl(incomingUrl?: string): void {
    const targetRoute = this.mapDeepLinkToRoute(incomingUrl);
    if (!targetRoute) {
      return;
    }

    void this.router.navigateByUrl(targetRoute);
  }

  private mapDeepLinkToRoute(incomingUrl?: string): string | null {
    if (!incomingUrl) {
      return null;
    }

    let parsed: URL;
    try {
      parsed = new URL(incomingUrl);
    } catch {
      return null;
    }

    const host = parsed.hostname.toLowerCase();
    const segments = parsed.pathname
      .split('/')
      .map((segment) => segment.trim())
      .filter((segment) => segment.length > 0);

    if (host === 'invite' || segments[0]?.toLowerCase() === 'invite') {
      const inviteCode =
        this.normalizeInviteCode(host === 'invite' ? segments[0] : segments[1]) ??
        this.normalizeInviteCode(parsed.searchParams.get('inviteCode'));
      if (!inviteCode) {
        return null;
      }

      const pollId = this.parsePollId(parsed.searchParams.get('pollId') ?? parsed.searchParams.get('poll'));
      if (!pollId) {
        return `/invite/${encodeURIComponent(inviteCode)}`;
      }

      return `/vote/${pollId}?inviteCode=${encodeURIComponent(inviteCode)}`;
    }

    if (host === 'vote' || segments[0]?.toLowerCase() === 'vote') {
      const pollId = this.parsePollId(host === 'vote' ? segments[0] : segments[1]);
      if (!pollId) {
        return null;
      }

      const inviteCode =
        this.normalizeInviteCode(parsed.searchParams.get('inviteCode')) ??
        this.normalizeInviteCode(parsed.searchParams.get('invite'));

      if (!inviteCode) {
        return `/vote/${pollId}`;
      }

      return `/vote/${pollId}?inviteCode=${encodeURIComponent(inviteCode)}`;
    }

    return null;
  }

  private parsePollId(value: string | null | undefined): number | null {
    if (!value) {
      return null;
    }

    const parsed = Number(value);
    if (!Number.isInteger(parsed) || parsed <= 0) {
      return null;
    }

    return parsed;
  }

  private normalizeInviteCode(value: string | null | undefined): string | null {
    if (!value) {
      return null;
    }

    const normalized = decodeURIComponent(value).trim().toUpperCase();
    return normalized.length > 0 ? normalized : null;
  }
}
