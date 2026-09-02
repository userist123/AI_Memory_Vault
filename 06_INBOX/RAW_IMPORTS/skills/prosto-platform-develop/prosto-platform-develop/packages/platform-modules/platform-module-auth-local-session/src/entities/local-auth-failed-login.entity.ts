import { Column, Entity, PrimaryColumn } from 'typeorm';

@Entity('auth_local_session_failed_logins')
export class LocalAuthFailedLoginEntity {
  @PrimaryColumn({ type: 'varchar', length: 255 })
  username!: string;

  @Column({ type: 'int' })
  failureCount!: number;

  @Column({ type: 'bigint' })
  windowStartedAt!: number;

  @Column({ type: 'bigint', nullable: true })
  blockedUntil!: number | null;
}
