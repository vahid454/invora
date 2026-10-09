import {inject,Injectable,signal} from '@angular/core';
import {ApiService,errorText} from './api';
export interface LicenseStatus {required:boolean;state:string;businessId:string|null;businessName:string;plan:string;expiresOn:string|null;graceUntil:string|null;canWrite:boolean}
@Injectable({providedIn:'root'})
export class ShopLicenseService {
 private api=inject(ApiService);readonly status=signal<LicenseStatus|null>(null);readonly error=signal('');
 async load(){try{this.status.set(await this.api.get<LicenseStatus>('/license'));this.error.set('');}catch(e){this.error.set(errorText(e));}}
 async activate(key:string){this.status.set(await this.api.mutate<LicenseStatus>('/license/activate',{key}));}
}
