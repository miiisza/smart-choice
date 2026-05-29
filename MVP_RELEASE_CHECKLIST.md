# Smart Choice MVP Release Checklist

Data checklisty: `2026-03-01`

## 1) Backend smoke (API)

- [ ] `GET /health/live` zwraca `200`.
- [ ] `GET /health/ready` zwraca `200`.
- [ ] `POST /api/auth/register` działa dla nowego użytkownika (`201`).
- [ ] `POST /api/auth/login` działa dla poprawnych danych (`200`) i zwraca `401 ProblemDetails` dla błędnych.
- [ ] `POST /api/auth/refresh` zwraca nową parę tokenów (`200`).
- [ ] `POST /api/auth/guest` działa dla aktywnego invite + `pollId` (`200`) i zwraca `400/404/409 ProblemDetails` dla błędnych/nieaktywnych danych.
- [ ] `POST /api/polls` (create draft) działa dla user token (`201`).
- [ ] `POST /api/polls/{id}/photos` działa dla draft author (`201`) i blokuje nieautoryzowane przypadki (`403` / `409`).
- [ ] `POST /api/polls/{id}/publish` przechodzi dla poprawnego draftu (`200`) i blokuje błędne przypadki (`409`).
- [ ] `POST /api/votes` zapisuje głos (`201`) i blokuje duplikaty (`409`).
- [ ] `GET /api/polls/{id}/results` zwraca wyniki i procenty (`200`).

## 2) Hardening smoke (rate limits + ProblemDetails + logi)

- [ ] `POST /api/auth/guest` po >8 req/min zwraca `429 ProblemDetails`.
- [ ] `POST /api/polls` po >5 req/min (ten sam user) zwraca `429 ProblemDetails`.
- [ ] `POST /api/votes` po >12 req/min (ten sam actor) zwraca `429 ProblemDetails`.
- [ ] Dla błędów `400/401/403/404/409/429` payload ma spójny format `ProblemDetails`.
- [ ] `ProblemDetails` zawiera `type`, `title`, `status`, `instance`, `traceId`.
- [ ] Błędy walidacyjne `400` zawierają `errors`.
- [ ] Log backendu zawiera zdarzenia: `guest token issue`, `poll draft create`, `vote create`, `duplicate vote`, `rate limit exceeded`.

## 3) Mobile smoke (Ionic/Angular)

- [ ] App startuje na web + mobile shell (Capacitor) bez crasha.
- [ ] Ekran create poll: logowanie autora działa i zapisuje sesję.
- [ ] Create poll wizard: upload 2-4 zdjęć + publish działa end-to-end.
- [ ] Invite redirect (`/invite/:inviteCode?pollId=...`) przekierowuje na `/vote/:pollId`.
- [ ] Vote screen: issue guest token działa, zdjęcia się ładują, głos przechodzi.
- [ ] Results screen pokazuje zwycięzcę, `voteCount`, `percentage`.
- [ ] Błędy API (np. 401/409/429) pokazują komunikat z `ProblemDetails` (nie generyczny stack).
- [ ] Przy braku sieci UI pokazuje komunikat offline i nie zawiesza flow.

## 4) Go/No-Go MVP

- [ ] Brak blockerów `P0/P1` otwartych na release.
- [ ] Migrations DB są zastosowane na środowisku docelowym.
- [ ] Konfiguracja `Auth`, `ConnectionStrings`, `ObjectStorage`, `Cors` ustawiona dla środowiska release.
- [ ] Monitoring/log collection działa (minimum: dostęp do logów API).
- [ ] Tag/revision release zapisany i możliwy rollback.
- [ ] Decyzja `GO` zatwierdzona przez ownera produktu.
