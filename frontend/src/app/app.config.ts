import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { ApiConfiguration } from '@caseauth/angular-client/src/api-configuration';

import { routes } from './app.routes';
import { devUserInterceptor } from './core/dev-user.interceptor';

// The backend only runs with the dev auth handler in Development (see the root README) -
// http://localhost:5020 is its default Kestrel binding from launchSettings.json's "http"
// profile. Point this at a different root if the API is running elsewhere.
const API_ROOT_URL = 'http://localhost:5020';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([devUserInterceptor])),
    { provide: ApiConfiguration, useValue: { rootUrl: API_ROOT_URL } },
  ],
};
