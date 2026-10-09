export interface User { id:string; login:string; displayName:string; isOwner:boolean; isActive:boolean; permissions:string[]; branchIds:string[] }
export interface Session { accessToken:string; expiresAtUtc:string; user:User }
export interface Branch { id:string; code:string; name:string; isActive:boolean }
export interface Page<T> { items:T[]; page:number; pageSize:number; totalItems:number; totalPages?:number }
export interface Product { id:string; name:string; brand:string; category:string; hsn:string; sku:string; barcode:string|null; ram:string; storage:string; color:string; serialized:boolean; sellingPrice:string; mrp:string; taxRateId:string; rate:string; cessRate:string; warrantyMonths:number; productModelId?:string; requiresImei?:boolean }
export interface Tax { id:string; name:string; rate:string; cessRate:string }
export interface Party {alternatePhone?:string; id:string; name:string; phone:string; email:string; address:string; stateCode:string; gstin:string; creditLimit:string|null }
export interface Unit { id:string; productVariantId:string; branchId:string; description:string; sku:string; condition:string; status:string; identifiers:string[]; cost:string|null; sellingPrice:string; receivedAtUtc:string }
export interface Document { id:string; number:string; status:string; total:string }
export type Row=Record<string,unknown>;
export interface Settings { legalName:string; address:string; phone:string; email:string; gstin:string; pan:string; stateCode:string; gstRegistered:boolean; compositionDealer:boolean; bankName:string; accountHolder:string; accountNumber:string; ifsc:string; bankBranch:string; upiId:string; declaration:string; jurisdiction:string; warrantyTerms:string; returnPolicy:string }

export interface CatalogCategory {name:string;requiresImei:boolean}
