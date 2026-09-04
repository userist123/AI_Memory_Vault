package com.nalo.medquery.domain.repository;

import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.jpa.repository.JpaRepository;

import com.nalo.medquery.domain.entity.PatientEntity;

public interface IPatientRepository extends JpaRepository<PatientEntity, Long>{

    Page<PatientEntity> findAllByAtivoTrue(Pageable page);
    
    boolean existsByIdAndAtivoTrue(Long patientId);

}
