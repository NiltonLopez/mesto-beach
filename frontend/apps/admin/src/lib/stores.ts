import { create } from "zustand";
import type { StaffDto } from "@mestobeach/api-client";

interface AuthState {
  staff: StaffDto | null;
  token: string | null;
  setAuth: (staff: StaffDto, token: string) => void;
  clearAuth: () => void;
}

export const useAuthStore = create<AuthState>()((set) => ({
  staff: null,
  token: null,
  setAuth: (staff, token) => set({ staff, token }),
  clearAuth: () => set({ staff: null, token: null }),
}));
