package com.nalo.medquery.domain.model.doctor;

import com.nalo.medquery.domain.entity.DoctorEntity;

public record DataListAllDoctors(
    Long id,
    String nome,
    String email,
    String crm, 
    EnumDoctorSpecialty especialidade
) {
    //construtor para conversão
    public DataListAllDoctors(DoctorEntity entity){
        this(entity.getId(), entity.getNome(), entity.getEmail(), entity.getCrm(), entity.getEspecialidade());
    }

}
