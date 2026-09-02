export interface PermissionItem {
  key: string;
  description: string;
}

export interface PermissionGroup {
  group: string;
  permissions: PermissionItem[];
}

export interface Role {
  id: string;
  name: string;
  isSystemDefault: boolean;
  permissionKeys: string[];
}

export interface CreateRoleInput {
  name: string;
  permissionKeys: string[];
}

export interface UpdateRoleInput {
  permissionKeys: string[];
}

export interface AdminUser {
  id: string;
  email: string;
  fullName: string;
  isActive: boolean;
  roleName: string | null;
  branchCount: number;
}

export interface InviteUserInput {
  email: string;
  fullName: string;
  phone?: string;
  roleId: string;
  branchId: string;
  expiresAt?: string;
}

export interface UserBranchGrant {
  branchId: string;
  branchName: string;
  grantedAt: string;
  expiresAt: string | null;
}

export interface GrantBranchInput {
  branchId: string;
  expiresAt?: string;
}
