package com.nalo.medquery.domain.repository;

import java.time.LocalDateTime;
import java.util.List;

import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;

import com.nalo.medquery.domain.entity.DoctorEntity;
import com.nalo.medquery.domain.model.doctor.EnumDoctorSpecialty;

public interface IDoctorRepository extends JpaRepository<DoctorEntity, Long> {

    Page<DoctorEntity> findAllByActiveTrue(Pageable page);

    @Query("""
        select d from doctor d
        where d.especialidade = :specialty
        and d.active = true
        and not exists (
            select 1 from appointment a
            where a.doctor = d
                and a.date = :date
        )
    """)
    List<DoctorEntity> findAvailableDoctors(EnumDoctorSpecialty specialty, LocalDateTime date);

    boolean existsByIdAndActiveTrue(Long doctorId);
    
}
