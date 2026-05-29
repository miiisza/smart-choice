# Smart Choice - lokalne uruchomienie DEV

Ta instrukcja uruchamia lokalnie cały stack na istniejących projektach:
- backend `.NET 10` z endpointami healthcheck i CORS
- frontend `Ionic`
- `MySQL 8` w Docker Compose (+ MinIO jako opcjonalna usługa legacy)

## 1) Wymagane zmienne środowiskowe

### Backend (`api/SmartChoice`)
Minimalny zestaw:

```bash
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5148
ConnectionStrings__Default=Server=localhost;Port=3306;Database=smart_choice;User=smart_choice;Password=smart_choice_dev;
SMARTCHOICE_CORS_ORIGINS=http://localhost:8100,http://127.0.0.1:8100,http://localhost,capacitor://localhost
Auth__Issuer=smart-choice-api
Auth__Audience=smart-choice-client
Auth__SigningKey=dev_only_change_me_to_a_long_32_char_secret_key
PHOTO_STORAGE_PROVIDER=LocalDisk
SIGNED_URL_TTL_MINUTES=5
ObjectStorage__BucketName=matchme-photos
ObjectStorage__Region=us-east-1
ObjectStorage__AccessKey=
ObjectStorage__SecretKey=
ObjectStorage__ServiceUrl=
ObjectStorage__PublicBaseUrl=http://localhost:5148
ObjectStorage__LocalDiskRootPath=App_Data/object-storage
ObjectStorage__LocalDiskSigningSecret=dev_local_storage_signing_secret_change_me
ObjectStorage__ForcePathStyle=true
ObjectStorage__ThumbnailWidth=480
ObjectStorage__MaxUploadBytes=10485760
```

Gotowiec: `api/SmartChoice/.env.example`

### Mobile / Ionic (`apk/smart-choice`)
Minimalny zestaw:

```bash
IONIC_API_BASE_URL=http://localhost:5148
```

Gotowiec: `apk/smart-choice/.env.example`

Uwaga dla emulatora Android: ustaw `IONIC_API_BASE_URL=http://10.0.2.2:5148`.

## 2) Docker Compose (MySQL 8 + opcjonalny MinIO)

Plik: `docker-compose.dev.yml`

- MySQL 8 (`localhost:3306`)
- MinIO (`localhost:9000`, `localhost:9001`) jest uruchamiany przez compose, ale przy `PHOTO_STORAGE_PROVIDER=LocalDisk` nie jest używany przez API.

## 3) Krok po kroku: jak odpalić wszystko lokalnie

Z katalogu repo:

```bash
# 1. Uruchom usługi infra (MySQL + opcjonalnie MinIO)
docker compose -f docker-compose.dev.yml up -d
```

Uruchom backend:

```bash
cd api/SmartChoice

# Linux/macOS
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS=http://localhost:5148
export ConnectionStrings__Default='Server=localhost;Port=3306;Database=smart_choice;User=smart_choice;Password=smart_choice_dev;'
export SMARTCHOICE_CORS_ORIGINS='http://localhost:8100,http://127.0.0.1:8100,http://localhost,capacitor://localhost'
export Auth__Issuer='smart-choice-api'
export Auth__Audience='smart-choice-client'
export Auth__SigningKey='dev_only_change_me_to_a_long_32_char_secret_key'
export PHOTO_STORAGE_PROVIDER='LocalDisk'
export SIGNED_URL_TTL_MINUTES=5
export ObjectStorage__BucketName='matchme-photos'
export ObjectStorage__Region='us-east-1'
export ObjectStorage__AccessKey=''
export ObjectStorage__SecretKey=''
export ObjectStorage__ServiceUrl=''
export ObjectStorage__PublicBaseUrl='http://localhost:5148'
export ObjectStorage__LocalDiskRootPath='App_Data/object-storage'
export ObjectStorage__LocalDiskSigningSecret='dev_local_storage_signing_secret_change_me'
export ObjectStorage__ForcePathStyle=true
export ObjectStorage__EnsureBucketExistsOnStartup=false
export ObjectStorage__MakeBucketPublicOnStartup=false
export ObjectStorage__ThumbnailWidth=480
export ObjectStorage__SignedUrlTtlMinutes=5
export ObjectStorage__MaxUploadBytes=10485760
export Database__AutoMigrateOnStartup=true
export Database__SeedDevDataOnStartup=true

# jednorazowo (jeśli nie masz): narzędzie migracji
dotnet tool install --global dotnet-ef

# aktualizacja schematu MySQL do najnowszej migracji
dotnet ef database update --project SmartChoice.csproj --startup-project SmartChoice.csproj

dotnet run --project SmartChoice.csproj
```

Sprawdź healthcheck backendu:

```bash
curl http://localhost:5148/health/live
curl http://localhost:5148/health/ready
```

Uruchom Ionic:

```bash
cd apk/smart-choice

# Linux/macOS
export IONIC_API_BASE_URL=http://localhost:5148

npm start
```

Frontend wystartuje na `http://localhost:8100`.

## 4) CORS i base URL - gdzie jest skonfigurowane

- Backend CORS:
  - `api/SmartChoice/SmartChoice/Program.cs` - polityka `SmartChoiceDevCors`
  - źródła są brane z `SMARTCHOICE_CORS_ORIGINS` (CSV), fallback: `Cors:AllowedOrigins` w `appsettings.Development.json`

- Backend healthcheck:
  - `GET /health/live` - liveness
  - `GET /health/ready` - readiness (sprawdza m.in. czy jest ustawiony `ConnectionStrings:Default`)
  - `GET /health` - redirect do `/health/ready`

- Ionic base URL:
  - `apk/smart-choice/scripts/write-runtime-env.mjs` generuje `src/assets/env.js` na podstawie `IONIC_API_BASE_URL`
  - `src/environments/environment.ts` i `environment.prod.ts` czytają `window.__env.API_BASE_URL`

## 5) Upload zdjęć (Ionic demo)

W aktualnym `folder` view (`/folder/inbox`) jest minimalny UI do uploadu zdjęć:
- podajesz `Poll ID` i `Bearer access token`,
- wybierasz do 4 plików,
- `Start upload` wysyła pliki na `POST /api/polls/{pollId}/photos`,
- każdy plik ma progress bar i automatyczny retry (`2` próby przy błędach `5xx`/network).
