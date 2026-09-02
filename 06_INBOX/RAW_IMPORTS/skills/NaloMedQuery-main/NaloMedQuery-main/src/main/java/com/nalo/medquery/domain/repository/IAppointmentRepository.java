package com.nalo.medquery.domain.repository;

import java.time.LocalDateTime;

import org.springframework.data.jpa.repository.JpaRepository;

import com.nalo.medquery.domain.entity.AppointmentEntity;

public interface IAppointmentRepository extends JpaRepository<AppointmentEntity, Long>{

    boolean existsByDoctor_IdAndDate(Long doctorId, LocalDateTime date);

    boolean existsByPatient_IdAndDate(Long patientId, LocalDateTime date);

}